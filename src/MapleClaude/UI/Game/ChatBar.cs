using MapleClaude.Render;
using MapleClaude.UI;
using MapleClaude.Wz;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace MapleClaude.UI.Game;

/// <summary>
/// In-game chat: a scrollable log + a text input, laid over the status bar's authentic chat area
/// (<c>StatusBar2.img/mainBar/chatEnter</c>). Enter focuses the input; Enter again sends and unfocuses;
/// the server echoes our own line back, so there is no local echo. Movement is gated while focused
/// (see GameStage's <c>TextField.Active</c> check).
/// </summary>
public sealed class ChatBar : GamePanel
{
    private const int MaxLines = 8;
    private const int LineHeight = 14;

    private readonly BuiltInFont? _font;
    private readonly TextField? _input;
    private readonly List<(string text, Color color)> _lines = new();

    /// <summary>The status bar owns the authentic chat-area rectangle (the <c>chatEnter</c> input box).</summary>
    public StatusBar? Bar { get; set; }

    public ChatBar(WzTextureLoader loader, WzPackage? ui, BuiltInFont? font)
    {
        _ = loader;
        _ = ui;
        _font = font;
        IsVisible = true;
        if (font != null)
        {
            _input = new TextField { Width = 480, Height = 16, Font = font, MaxLength = 70 };
        }
        AddLine("Welcome to MapleClaude!", new Color(220, 200, 100));
    }

    public void AddLine(string text, Color? color = null)
    {
        _lines.Add((text, color ?? Color.White));
        if (_lines.Count > 100) _lines.RemoveAt(0);
    }

    /// <summary>The chat input box in screen space — the status bar's <c>chatEnter</c> when available,
    /// else a bottom-left fallback.</summary>
    private Rectangle InputRect => Bar?.ChatInputRect ?? new Rectangle(8, 540, 480, 18);

    public override void Relayout(int viewWidth, int viewHeight)
    {
        _ = viewWidth;
        _ = viewHeight;   // layout follows the status bar's chat rect; nothing fixed to compute here
    }

    public override void Update(GameTime gameTime)
    {
        if (_input != null)
        {
            var r = InputRect;
            _input.Position = new Vector2(r.X + 6, r.Y + (r.Height - _input.Height) / 2f);
            _input.Update(gameTime);
        }
    }

    public override void Draw(SpriteBatch sb, Texture2D white)
    {
        if (!IsVisible) return;

        var r = InputRect;

        // Chat log: newest at the bottom, stacked upward just above the input box, on a subtle backdrop.
        var visibleStart = Math.Max(0, _lines.Count - MaxLines);
        var count = _lines.Count - visibleStart;
        if (count > 0)
        {
            var bottom = r.Y - 2;
            var top = bottom - count * LineHeight;
            sb.Draw(white, new Rectangle(r.X, top - 2, Math.Max(r.Width, 320), count * LineHeight + 4), new Color(0, 0, 0, 110));
            for (var i = 0; i < count; i++)
            {
                var ln = _lines[visibleStart + i];
                _font?.Draw(sb, ln.text, new Vector2(r.X + 4, top + i * LineHeight), ln.color);
            }
        }

        _input?.Draw(sb, white);
    }

    public override bool HandleMouseButton(int x, int y, bool down)
    {
        if (!IsVisible) return false;
        _input?.HandleMouseButton(x, y, down);
        return InputRect.Contains(x, y);
    }

    /// <summary>Called when the user presses Enter to send a message. Wire to <c>GameSender.UserChat</c>.</summary>
    public Action<string>? OnSendChat { get; set; }

    public override bool OnKeyPress(Keys key)
    {
        if (!IsVisible) return false;
        if (_input?.IsFocused == true)
        {
            if (key == Keys.Enter)
            {
                var text = _input.Text.Trim();
                // No local echo: the server broadcasts our own chat back (UserChat), which adds the line.
                if (text.Length > 0) OnSendChat?.Invoke(text);
                _input.Clear();
                _input.IsFocused = false;   // release focus so movement keys work again
                return true;
            }
            if (key == Keys.Escape) { _input.Clear(); _input.IsFocused = false; return true; }
            if (key == Keys.Back) { _input.OnTextInput('\b'); return true; }
            _input.OnKeyPress(key, Keyboard.GetState());   // caret / Ctrl-A/C/V/X while typing
            return true;                                   // swallow keys so they don't fire game hotkeys
        }
        if (key == Keys.Enter && _input != null)   // Enter when unfocused → focus the chat input
        {
            _input.IsFocused = true;
            return true;
        }
        return false;
    }

    public override void OnTextInput(char ch)
    {
        if (_input?.IsFocused == true) _input.OnTextInput(ch);
    }
}
