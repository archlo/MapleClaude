using System.Text;
using MapleClaude.Platform;
using MapleClaude.Render;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace MapleClaude.UI;

/// <summary>
/// Text input field with caret + selection + Ctrl-A/C/V/X clipboard support
/// (via WinForms <c>Clipboard</c>). Renders the supplied WZ background sprite
/// when no text is typed; switches to the real text once anything is entered.
/// Password mode renders dots instead of glyphs.
/// </summary>
public sealed class TextField
{
    private readonly StringBuilder _text = new();
    private float _caretTimer;
    private bool _caretVisible = true;
    private int _caret;
    private int _selectionStart = -1; // -1 = no selection
    private int _scrollPx;            // pixel offset of the visible text window into the full string
    private bool _isFocused;
    private string _composition = string.Empty; // live IME preedit (un-committed) text

    /// <summary>The field that currently has focus, so the IME composition hook can route
    /// the live preedit string to it without per-stage plumbing. Only one field is focused
    /// at a time in this single-window client.</summary>
    internal static TextField? Active { get; private set; }

    public Vector2 Position { get; set; }
    public int Width { get; set; } = 160;
    public int Height { get; set; } = 24;

    public bool IsFocused
    {
        get => _isFocused;
        set
        {
            _isFocused = value;
            if (value)
            {
                Active = this;
            }
            else if (ReferenceEquals(Active, this))
            {
                Active = null;
                _composition = string.Empty;
            }
        }
    }
    public bool IsPassword { get; set; }
    public int MaxLength { get; set; } = 24;
    public WzSprite? Background { get; set; }
    /// <summary>When there is no <see cref="Background"/> sprite, draw a plain fallback box.
    /// Set false to type directly over existing UI art (e.g. a wooden input recess).</summary>
    public bool DrawFallbackBox { get; set; } = true;
    public BuiltInFont? Font { get; set; }
    /// <summary>Colour of the typed text (default near-black for light panels).</summary>
    public Color TextColor { get; set; } = Color.Black;
    /// <summary>Caret colour (default black; e.g. cyan to stand out on a dark input recess).</summary>
    public Color CaretColor { get; set; } = Color.Black;

    public string Text
    {
        get => _text.ToString();
        set
        {
            _text.Clear();
            if (!string.IsNullOrEmpty(value))
            {
                _text.Append(value);
            }
            _caret = _text.Length;
            _selectionStart = -1;
        }
    }

    public Rectangle Bounds => new((int)Position.X, (int)Position.Y, Width, Height);

    public void Clear()
    {
        _text.Clear();
        _caret = 0;
        _selectionStart = -1;
        _composition = string.Empty;
    }

    /// <summary>Sets the live IME composition (preedit) string drawn at the caret. The
    /// committed text still arrives via <see cref="OnTextInput"/>; this is the transient
    /// "being typed" syllable shown underlined.</summary>
    public void SetComposition(string composition) => _composition = composition ?? string.Empty;

    /// <summary>Routes an IME composition update to whichever field is focused.</summary>
    internal static void SetActiveComposition(string composition) => Active?.SetComposition(composition);

    /// <summary>Drops focus from the active field. Called on stage transitions so a dead field's focus
    /// (e.g. login id/pw) doesn't leak into gameplay and keep the IME Right-Alt toggle armed.</summary>
    internal static void ClearActive()
    {
        var a = Active;
        if (a != null) a.IsFocused = false;   // the IsFocused setter nulls Active
    }

    public void OnTextInput(char ch)
    {
        if (!IsFocused)
        {
            return;
        }
        // A committed character ends the current composition; clearing it here avoids a
        // one-frame overlap of the preedit glyph and the inserted character.
        _composition = string.Empty;
        if (ch == '\b')
        {
            if (HasSelection)
            {
                DeleteSelection();
            }
            else if (_caret > 0)
            {
                _text.Remove(_caret - 1, 1);
                _caret--;
            }
            return;
        }
        if (ch is < ' ' or '\x7F')
        {
            return;
        }
        if (HasSelection)
        {
            DeleteSelection();
        }
        if (_text.Length >= MaxLength)
        {
            return;
        }
        _text.Insert(_caret, ch);
        _caret++;
    }

    /// <summary>Returns true if the click was inside the field (changing focus).</summary>
    public bool HandleMouseButton(int x, int y, bool down)
    {
        if (!down)
        {
            return false;
        }
        var inside = Bounds.Contains(x, y);
        IsFocused = inside;
        if (inside)
        {
            _selectionStart = -1;
            _caret = _text.Length;
        }
        return inside;
    }

    /// <summary>Process a non-character key (Ctrl-combos, arrows, Home/End, Delete). Returns true if consumed.</summary>
    public bool OnKeyPress(Keys key, KeyboardState kb)
    {
        if (!IsFocused)
        {
            return false;
        }
        var ctrl = kb.IsKeyDown(Keys.LeftControl) || kb.IsKeyDown(Keys.RightControl);
        var shift = kb.IsKeyDown(Keys.LeftShift) || kb.IsKeyDown(Keys.RightShift);
        if (ctrl)
        {
            switch (key)
            {
                case Keys.A:
                    _selectionStart = 0;
                    _caret = _text.Length;
                    return true;
                case Keys.C:
                    CopyToClipboard();
                    return true;
                case Keys.X:
                    CopyToClipboard();
                    if (HasSelection)
                    {
                        DeleteSelection();
                    }
                    return true;
                case Keys.V:
                    PasteFromClipboard();
                    return true;
            }
        }
        switch (key)
        {
            case Keys.Left:
                EnsureSelectionAnchor(shift);
                if (_caret > 0) { _caret--; }
                return true;
            case Keys.Right:
                EnsureSelectionAnchor(shift);
                if (_caret < _text.Length) { _caret++; }
                return true;
            case Keys.Home:
                EnsureSelectionAnchor(shift);
                _caret = 0;
                return true;
            case Keys.End:
                EnsureSelectionAnchor(shift);
                _caret = _text.Length;
                return true;
            case Keys.Delete:
                if (HasSelection)
                {
                    DeleteSelection();
                }
                else if (_caret < _text.Length)
                {
                    _text.Remove(_caret, 1);
                }
                return true;
        }
        return false;
    }

    public void Update(GameTime gameTime)
    {
        _caretTimer += (float)gameTime.ElapsedGameTime.TotalSeconds;
        if (_caretTimer >= 0.5f)
        {
            _caretTimer -= 0.5f;
            _caretVisible = !_caretVisible;
        }
    }

    public void Draw(SpriteBatch sb, Texture2D whitePixel)
    {
        var hasText = _text.Length > 0;
        if (Background is not null)
        {
            if (!hasText)
            {
                Background.Draw(sb, Position);
            }
        }
        else if (DrawFallbackBox)
        {
            sb.Draw(whitePixel, Bounds, new Color(230, 230, 230));
            sb.Draw(whitePixel, new Rectangle(Bounds.X, Bounds.Y, Bounds.Width, 1), Color.DarkGray);
            sb.Draw(whitePixel, new Rectangle(Bounds.X, Bounds.Y + Bounds.Height - 1, Bounds.Width, 1), Color.DarkGray);
        }

        const int textPadX = 6;
        var visibleWidth = Math.Max(0, Width - textPadX * 2);
        int caretX;
        if (!IsPassword && Font is not null)
        {
            // Insert the live IME composition at the caret for display; the visual caret
            // sits after it so the user is typing "into" the preedit. Committed text
            // (_text) is unaffected — the composition is transient until WM_CHAR commits.
            var committed = _text.ToString();
            var composing = IsFocused && _composition.Length > 0;
            var fullText = composing
                ? string.Concat(committed.AsSpan(0, _caret), _composition, committed.AsSpan(_caret))
                : committed;
            var caretIdx = composing ? _caret + _composition.Length : _caret;
            var textY = (int)Position.Y + (Height - Font.LineHeight) / 2;
            var caretPx = (int)Font.Measure(fullText[..caretIdx]).X;

            // Keep the caret inside the field by scrolling the text horizontally.
            // Caret-on-right edge: snap _scrollPx so the caret hugs the right side.
            // Caret-on-left edge: snap so the caret hugs the left side.
            if (caretPx - _scrollPx > visibleWidth)
            {
                _scrollPx = caretPx - visibleWidth;
            }
            if (caretPx - _scrollPx < 0)
            {
                _scrollPx = caretPx;
            }
            if (_scrollPx < 0)
            {
                _scrollPx = 0;
            }

            var leftX = (int)Position.X + textPadX;
            var rightX = leftX + visibleWidth;

            // Selection highlight, clipped to the visible window. Hidden while composing.
            if (HasSelection && IsFocused && !composing)
            {
                var (lo, hi) = OrderedSelection();
                var loPx = (int)Font.Measure(fullText[..lo]).X;
                var hiPx = (int)Font.Measure(fullText[..hi]).X;
                var screenLo = Math.Max(leftX, leftX + (loPx - _scrollPx));
                var screenHi = Math.Min(rightX, leftX + (hiPx - _scrollPx));
                if (screenHi > screenLo)
                {
                    var rect = new Rectangle(screenLo, textY, screenHi - screenLo, Font.LineHeight);
                    sb.Draw(whitePixel, rect, new Color(80, 130, 220, 180));
                }
            }

            // Find the first/last chars visible inside [_scrollPx, _scrollPx + visibleWidth].
            // Linear scan — fine for our short (≤ 64-char) login/password fields.
            var firstIdx = 0;
            for (var i = 0; i <= fullText.Length; i++)
            {
                if ((int)Font.Measure(fullText[..i]).X >= _scrollPx)
                {
                    firstIdx = Math.Max(0, i - 1);
                    break;
                }
                firstIdx = i;
            }
            var lastIdx = firstIdx;
            for (var i = firstIdx + 1; i <= fullText.Length; i++)
            {
                if ((int)Font.Measure(fullText[..i]).X > _scrollPx + visibleWidth + 8)
                {
                    break;
                }
                lastIdx = i;
            }
            if (lastIdx > firstIdx)
            {
                var visible = fullText[firstIdx..lastIdx];
                var firstPx = (int)Font.Measure(fullText[..firstIdx]).X;
                Font.Draw(sb, visible, new Vector2(leftX + (firstPx - _scrollPx), textY), TextColor);
            }

            // Underline the composition span so it reads as an IME preedit.
            if (composing)
            {
                var underStart = (int)Font.Measure(fullText[.._caret]).X;
                var underEnd = (int)Font.Measure(fullText[..caretIdx]).X;
                var sx = Math.Max(leftX, leftX + (underStart - _scrollPx));
                var ex = Math.Min(rightX, leftX + (underEnd - _scrollPx));
                if (ex > sx)
                {
                    sb.Draw(whitePixel, new Rectangle(sx, textY + Font.LineHeight - 1, ex - sx, 1), TextColor);
                }
            }

            caretX = leftX + (caretPx - _scrollPx);
        }
        else
        {
            var dotColor = IsPassword ? Color.Black : new Color(40, 40, 60);
            const int dotSize = 4;
            const int dotSpacing = 7;
            var startX = (int)Position.X + textPadX;
            var dotY = (int)Position.Y + (Height - dotSize) / 2;
            // Dot mode: scroll by dot units so the caret stays visible.
            var caretDotPx = _caret * dotSpacing;
            if (caretDotPx - _scrollPx > visibleWidth)
            {
                _scrollPx = caretDotPx - visibleWidth;
            }
            if (caretDotPx - _scrollPx < 0)
            {
                _scrollPx = caretDotPx;
            }
            if (_scrollPx < 0)
            {
                _scrollPx = 0;
            }
            for (var i = 0; i < _text.Length; i++)
            {
                var dotX = startX + i * dotSpacing - _scrollPx;
                if (dotX + dotSize < startX)
                {
                    continue;
                }
                if (dotX > startX + visibleWidth)
                {
                    break;
                }
                sb.Draw(whitePixel, new Rectangle(dotX, dotY, dotSize, dotSize), dotColor);
            }
            caretX = startX + caretDotPx - _scrollPx;
        }

        // Caret only renders if it's inside the visible window.
        if (IsFocused && _caretVisible && !HasSelection)
        {
            var leftEdge = (int)Position.X + textPadX;
            var rightEdge = leftEdge + visibleWidth;
            if (caretX >= leftEdge && caretX <= rightEdge)
            {
                sb.Draw(whitePixel, new Rectangle(caretX, (int)Position.Y + 4, 1, Height - 8), CaretColor);
            }
        }
    }

    private bool HasSelection => _selectionStart >= 0 && _selectionStart != _caret;

    private (int Lo, int Hi) OrderedSelection()
    {
        var lo = Math.Min(_selectionStart, _caret);
        var hi = Math.Max(_selectionStart, _caret);
        lo = Math.Clamp(lo, 0, _text.Length);
        hi = Math.Clamp(hi, 0, _text.Length);
        return (lo, hi);
    }

    private void EnsureSelectionAnchor(bool shift)
    {
        if (shift)
        {
            if (_selectionStart < 0)
            {
                _selectionStart = _caret;
            }
        }
        else
        {
            _selectionStart = -1;
        }
    }

    private void DeleteSelection()
    {
        if (!HasSelection)
        {
            return;
        }
        var (lo, hi) = OrderedSelection();
        _text.Remove(lo, hi - lo);
        _caret = lo;
        _selectionStart = -1;
    }

    private void CopyToClipboard()
    {
        // Don't expose password content via clipboard.
        if (IsPassword)
        {
            return;
        }
        string toCopy;
        if (HasSelection)
        {
            var (lo, hi) = OrderedSelection();
            toCopy = _text.ToString(lo, hi - lo);
        }
        else
        {
            toCopy = _text.ToString();
        }
        if (!string.IsNullOrEmpty(toCopy))
        {
            // ClipboardHelper uses direct user32 P/Invoke — works from the
            // MonoGame main thread (which runs MTA; WinForms Clipboard would throw).
            ClipboardHelper.SetText(toCopy);
        }
    }

    private void PasteFromClipboard()
    {
        var clip = ClipboardHelper.GetText();
        if (string.IsNullOrEmpty(clip))
        {
            return;
        }
        // Drop control chars (newline, tab, etc.) — single-line field.
        var sb = new StringBuilder(clip.Length);
        foreach (var c in clip)
        {
            if (c < ' ' || c == '\x7F')
            {
                continue;
            }
            sb.Append(c);
        }
        var insert = sb.ToString();
        if (insert.Length == 0)
        {
            return;
        }
        if (HasSelection)
        {
            DeleteSelection();
        }
        var roomLeft = MaxLength - _text.Length;
        if (roomLeft <= 0)
        {
            return;
        }
        if (insert.Length > roomLeft)
        {
            insert = insert[..roomLeft];
        }
        _text.Insert(_caret, insert);
        _caret += insert.Length;
    }
}
