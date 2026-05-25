using MapleClaude.Render;
using MapleClaude.UI;
using MapleClaude.Wz;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace MapleClaude.UI.Game;

/// <summary>
/// NPC dialog panel. Shown when the server sends NPC script packets.
/// WZ: <c>UIWindow.img/NpcTalk/</c>
/// </summary>
public sealed class NpcTalk : GamePanel
{
    public enum DialogType { Ok, YesNo, Next, PrevNext, Menu, AskText, AskNumber, Quiz }

    private readonly WzTextureLoader _loader;
    private readonly WzSprite? _background;
    private readonly Button? _btOk;
    private readonly Button? _btYes;
    private readonly Button? _btNo;
    private readonly Button? _btNext;
    private readonly Button? _btPrev;
    private readonly BuiltInFont? _font;
    private readonly List<Button> _allButtons = new();
    private readonly TextField _inputField = new() { Width = 300, Height = 22 };

    private string _text = string.Empty;
    private DialogType _dialogType = DialogType.Ok;
    private List<string> _menuItems = new();
    private int _menuHover = -1;
    private int _minNum, _maxNum;
    private bool _numberOnly;

    // Portrait (speaker NPC face)
    private WzSprite? _portrait;

    // Quiz timer
    private float _quizTimerSec;

    // ── Callbacks ─────────────────────────────────────────────────────────────
    public Action? OnOk { get; set; }
    public Action? OnYes { get; set; }
    public Action? OnNo { get; set; }
    public Action? OnNext { get; set; }
    public Action? OnPrev { get; set; }
    public Action<int>? OnMenuChoice { get; set; }
    public Action<string>? OnTextConfirm { get; set; }
    public Action<int>? OnNumberConfirm { get; set; }

    public NpcTalk(WzTextureLoader loader, WzPackage? ui, BuiltInFont? font)
    {
        _loader = loader;
        _font = font;
        _inputField.Font = font;
        IsVisible = false;
        Position = new Vector2(170, 380);

        var npc = ui?.GetItem("UIWindow.img/NpcTalk") as WzProperty;
        _background = npc?.Get("backgrnd") is WzCanvas bc ? loader.Load(bc) : null;

        _btOk   = MakeButton(loader, npc, "BtOK");
        _btYes  = MakeButton(loader, npc, "BtYes");
        _btNo   = MakeButton(loader, npc, "BtNo");
        _btNext = MakeButton(loader, npc, "BtNext");
        _btPrev = MakeButton(loader, npc, "BtPrev");

        if (_btOk   != null) _btOk.OnClick   = HandleOk;
        if (_btYes  != null) _btYes.OnClick  = HandleYes;
        if (_btNo   != null) _btNo.OnClick   = HandleNo;
        if (_btNext != null) _btNext.OnClick = HandleNext;
        if (_btPrev != null) _btPrev.OnClick = HandlePrev;
    }

    // ── Portrait ──────────────────────────────────────────────────────────────

    /// <summary>Load NPC portrait from Npc.wz for the given template ID. Safe with null package.</summary>
    public void LoadPortrait(WzPackage? npcWz, int speakerId)
    {
        _portrait = null;
        if (npcWz is null || speakerId <= 0) return;
        if (npcWz.GetItem($"{speakerId:D7}.img") is not WzImage img) return;
        var root = img.Root;
        // Try common animation states to find a first frame
        foreach (var stateName in (string[])["stand", "walk", "move", "idle", "default"])
        {
            if (root?.Get(stateName) is not WzProperty stateNode) continue;
            var raw = stateNode.Get("0");
            WzCanvas? canvas = raw switch
            {
                WzCanvas c => c,
                WzProperty fp => fp.Items.Select(kv => kv.Value as WzCanvas).FirstOrDefault(c => c is not null),
                _ => null,
            };
            if (canvas is null) continue;
            _portrait = _loader.Load(canvas);
            break;
        }
    }

    // ── Show methods ──────────────────────────────────────────────────────────

    public void Show(string text, DialogType type = DialogType.Ok)
    {
        _text       = text;
        _dialogType = type;
        _menuItems.Clear();
        IsVisible   = true;
    }

    public void ShowMenu(string text, IReadOnlyList<string> items)
    {
        _text       = text;
        _dialogType = DialogType.Menu;
        _menuItems  = new List<string>(items);
        _menuHover  = -1;
        IsVisible   = true;
    }

    public void ShowAskText(string text, string defaultText = "", int minLen = 0, int maxLen = 24)
    {
        _text            = text;
        _dialogType      = DialogType.AskText;
        _menuItems.Clear();
        _numberOnly      = false;
        _inputField.Text = defaultText;
        _inputField.MaxLength = Math.Max(1, maxLen > 0 ? maxLen : 24);
        _inputField.IsFocused = true;
        IsVisible        = true;
    }

    public void ShowAskNumber(string text, int defaultNum = 0, int minNum = 0, int maxNum = 999999)
    {
        _text            = text;
        _dialogType      = DialogType.AskNumber;
        _menuItems.Clear();
        _numberOnly      = true;
        _minNum          = minNum;
        _maxNum          = maxNum;
        _inputField.Text = defaultNum.ToString();
        _inputField.MaxLength = 10;
        _inputField.IsFocused = true;
        IsVisible        = true;
    }

    public void ShowQuiz(string text, string hint = "", int minLen = 0, int maxLen = 24, int remainSec = 60)
    {
        _text            = text;
        _dialogType      = DialogType.Quiz;
        _menuItems.Clear();
        _numberOnly      = false;
        _inputField.Text = hint;
        _inputField.MaxLength = Math.Max(1, maxLen > 0 ? maxLen : 24);
        _inputField.IsFocused = true;
        _quizTimerSec    = Math.Max(1f, remainSec);
        IsVisible        = true;
    }

    // ── Button handlers ───────────────────────────────────────────────────────

    private void HandleOk()
    {
        switch (_dialogType)
        {
            case DialogType.AskText:
            case DialogType.Quiz:
                var txt = _inputField.Text;
                _inputField.Clear();
                IsVisible = false;
                OnTextConfirm?.Invoke(txt);
                break;
            case DialogType.AskNumber:
                var raw = _inputField.Text;
                _inputField.Clear();
                IsVisible = false;
                if (int.TryParse(raw, out var num))
                    OnNumberConfirm?.Invoke(Math.Clamp(num, _minNum, _maxNum));
                break;
            default:
                IsVisible = false;
                OnOk?.Invoke();
                break;
        }
    }

    private void HandleYes()  { IsVisible = false; OnYes?.Invoke(); }
    private void HandleNo()   { IsVisible = false; OnNo?.Invoke(); }
    private void HandleNext() { OnNext?.Invoke(); }
    private void HandlePrev() { OnPrev?.Invoke(); }

    // ── Layout ────────────────────────────────────────────────────────────────

    private int DialogHeight => _dialogType switch
    {
        DialogType.Menu      => 80 + Math.Max(1, _menuItems.Count) * 20 + 10,
        DialogType.AskText   => 160,
        DialogType.AskNumber => 160,
        DialogType.Quiz      => 170,
        _                    => 130,
    };

    private void ApplyLayout()
    {
        var h = DialogHeight;
        var btnY = Position.Y + h - 26;
        if (_btOk   != null) _btOk.Position   = new Vector2(Position.X + 430, btnY);
        if (_btYes  != null) _btYes.Position  = new Vector2(Position.X + 390, btnY);
        if (_btNo   != null) _btNo.Position   = new Vector2(Position.X + 430, btnY);
        if (_btNext != null) _btNext.Position = new Vector2(Position.X + 430, btnY);
        if (_btPrev != null) _btPrev.Position = new Vector2(Position.X + 390, btnY);
        _inputField.Position = new Vector2(Position.X + 70, Position.Y + 85);
    }

    public override void Update(GameTime gameTime)
    {
        ApplyLayout();
        // A hidden dialog must not keep the text-input focus (else Right Alt stays armed for the IME).
        if (!IsVisible && _inputField.IsFocused) _inputField.IsFocused = false;
        if (_dialogType == DialogType.Quiz && IsVisible && _quizTimerSec > 0)
        {
            _quizTimerSec -= (float)gameTime.ElapsedGameTime.TotalSeconds;
            if (_quizTimerSec <= 0)
            {
                _quizTimerSec = 0;
                IsVisible = false;
                OnNo?.Invoke();   // timeout = cancel
            }
        }
    }

    // ── Draw ──────────────────────────────────────────────────────────────────

    public override void Draw(SpriteBatch sb, Texture2D white)
    {
        if (!IsVisible) return;

        ApplyLayout();
        var h = DialogHeight;

        // NPC portrait — drawn above-left of the dialog box
        if (_portrait != null)
            _portrait.Draw(sb, Position + new Vector2(6, -90));

        // Background
        if (_background != null)
            _background.Draw(sb, Position + new Vector2(234, 65));
        else
        {
            sb.Draw(white, new Rectangle((int)Position.X, (int)Position.Y, 470, h), new Color(15, 15, 25, 230));
            DrawBorder(sb, white, new Rectangle((int)Position.X, (int)Position.Y, 470, h));
        }

        // Main text
        DrawText(sb, _text, Position + new Vector2(10, 10), Color.White, 450);

        switch (_dialogType)
        {
            case DialogType.Ok:
                _btOk?.Draw(sb);
                break;
            case DialogType.YesNo:
                _btYes?.Draw(sb);
                _btNo?.Draw(sb);
                break;
            case DialogType.Next:
                _btNext?.Draw(sb);
                break;
            case DialogType.PrevNext:
                _btPrev?.Draw(sb);
                _btNext?.Draw(sb);
                break;
            case DialogType.Menu:
                DrawMenuItems(sb, white);
                break;
            case DialogType.AskText:
            case DialogType.AskNumber:
                DrawInputField(sb, white);
                _btOk?.Draw(sb);
                _btNo?.Draw(sb);
                break;
            case DialogType.Quiz:
                DrawInputField(sb, white);
                // Countdown timer
                var secs     = (int)Math.Ceiling(_quizTimerSec);
                var timerStr = $"Time: {secs}s";
                var timerClr = _quizTimerSec < 10 ? new Color(255, 80, 80) : new Color(220, 200, 100);
                _font?.Draw(sb, timerStr, Position + new Vector2(340, 88), timerClr);
                _btOk?.Draw(sb);
                _btNo?.Draw(sb);
                break;
        }
    }

    private void DrawMenuItems(SpriteBatch sb, Texture2D white)
    {
        if (_font is null) return;
        var lineH = _font.LineHeight + 4;
        for (var i = 0; i < _menuItems.Count; i++)
        {
            var itemPos = new Vector2(Position.X + 10, Position.Y + 72 + i * lineH);
            var itemBg  = new Rectangle((int)itemPos.X - 2, (int)itemPos.Y - 1, 450, lineH);
            if (_menuHover == i)
                sb.Draw(white, itemBg, new Color(80, 70, 40, 160));
            var color = _menuHover == i ? new Color(255, 220, 80) : Color.LightYellow;
            _font.Draw(sb, $"{i + 1}. {_menuItems[i]}", itemPos, color);
        }
    }

    private void DrawInputField(SpriteBatch sb, Texture2D white)
    {
        var bounds = _inputField.Bounds;
        sb.Draw(white, bounds, new Color(20, 20, 35, 220));
        DrawBorder(sb, white, bounds);
        _inputField.Draw(sb, white);
    }

    private void DrawText(SpriteBatch sb, string text, Vector2 origin, Color color, int maxWidth)
    {
        if (_font is null || string.IsNullOrEmpty(text)) return;
        var lh    = _font.LineHeight + 2;
        var pos   = origin;
        var clean = StripFormatCodes(text);
        foreach (var rawLine in clean.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            _font.Draw(sb, line, pos, color);
            pos.Y += lh;
        }
    }

    private static string StripFormatCodes(string text)
    {
        var sb = new System.Text.StringBuilder(text.Length);
        var i = 0;
        while (i < text.Length)
        {
            if (text[i] == '#' && i + 1 < text.Length)
            {
                if (text[i + 1] == 'L')
                {
                    var j = i + 2;
                    while (j < text.Length && char.IsDigit(text[j])) j++;
                    if (j < text.Length && text[j] == '#') { i = j + 1; continue; }
                }
                if (text[i + 1] == 'l') { i += 2; continue; }
                if (char.IsLetter(text[i + 1])) { i += 2; continue; }
            }
            sb.Append(text[i]);
            i++;
        }
        return sb.ToString();
    }

    // ── Input routing ─────────────────────────────────────────────────────────

    public override bool HandleMouseButton(int x, int y, bool down)
    {
        if (!IsVisible) return false;

        foreach (var b in _allButtons)
            if (b.HandleMouseButton(x, y, down)) return true;

        if (_dialogType == DialogType.Menu && _menuItems.Count > 0)
        {
            var lh = (_font?.LineHeight ?? 14) + 4;
            for (var i = 0; i < _menuItems.Count; i++)
            {
                var itemRect = new Rectangle(
                    (int)Position.X + 8,
                    (int)Position.Y + 71 + i * lh,
                    450, lh);
                if (itemRect.Contains(x, y))
                {
                    if (down) _menuHover = i;
                    else { IsVisible = false; OnMenuChoice?.Invoke(i); }
                    return true;
                }
            }
            _menuHover = -1;
        }

        if (_dialogType is DialogType.AskText or DialogType.AskNumber or DialogType.Quiz)
            _inputField.HandleMouseButton(x, y, down);

        return new Rectangle((int)Position.X, (int)Position.Y, 470, DialogHeight).Contains(x, y);
    }

    public override bool OnKeyPress(Keys key)
    {
        if (!IsVisible) return false;

        if (_dialogType is DialogType.AskText or DialogType.AskNumber or DialogType.Quiz)
        {
            var kb = Keyboard.GetState();
            if (_inputField.OnKeyPress(key, kb)) return true;
            if (key == Keys.Enter)  { HandleOk(); return true; }
            if (key == Keys.Escape) { IsVisible = false; OnNo?.Invoke(); return true; }
            return true;
        }

        if (key == Keys.Enter)  { HandleOk(); return true; }
        if (key == Keys.Escape) { IsVisible = false; OnNo?.Invoke(); return true; }
        return true;
    }

    public override void OnTextInput(char character)
    {
        if (!IsVisible) return;
        if (_dialogType is DialogType.AskText or DialogType.AskNumber or DialogType.Quiz)
        {
            if (_numberOnly && !char.IsDigit(character) && character != '-') return;
            _inputField.OnTextInput(character);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private Button? MakeButton(WzTextureLoader loader, WzProperty? root, string name)
    {
        var pr = root?.Get(name) as WzProperty;
        if (pr is null) return null;
        var b = new Button(loader, pr);
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
