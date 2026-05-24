using System.Text;
using MapleClaude.Render;
using MapleClaude.Wz;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace MapleClaude.UI;

/// <summary>
/// Modal notice dialog shown during the login flow (wrong password, blocked account, etc.).
/// WZ: <c>Login.img/Notice/</c> — falls back to a drawn rectangle if assets missing.
/// </summary>
public sealed class LoginNoticeOverlay : Overlay
{
    public enum NoticeType
    {
        Ok,
        OkCancel,
    }

    private readonly WzSprite? _background;
    private readonly Button? _btOk;
    private readonly Button? _btCancel;
    private readonly BuiltInFont? _font;
    private readonly Vector2 _center;

    private string _message = string.Empty;
    private NoticeType _type;
    private float _autoDismiss;   // seconds left before auto-close (0 = stay until OK/ESC)

    public Action? OnOk { get; set; }
    public Action? OnCancel { get; set; }

    public LoginNoticeOverlay(WzTextureLoader loader, WzPackage? ui, BuiltInFont? font, Vector2 center)
    {
        _font = font;
        _center = center;

        var notice = ui?.GetItem("Login.img/Notice") as WzProperty;
        _background = notice?.Get("backgrnd") is WzCanvas bg ? loader.Load(bg) : null;

        var okRoot = notice?.Get("BtOK") as WzProperty;
        if (okRoot != null)
        {
            _btOk = new Button(loader, okRoot) { OnClick = () => { IsVisible = false; OnOk?.Invoke(); } };
        }
        var cancelRoot = notice?.Get("BtCancel") as WzProperty;
        if (cancelRoot != null)
        {
            _btCancel = new Button(loader, cancelRoot) { OnClick = () => { IsVisible = false; OnCancel?.Invoke(); } };
        }
    }

    public void Show(string message, NoticeType type = NoticeType.Ok, float autoDismissSeconds = 0f)
    {
        _message = message;
        _type = type;
        _autoDismiss = autoDismissSeconds;
        IsVisible = true;

        var ox = _type == NoticeType.OkCancel ? -40f : 0f;
        if (_btOk != null) _btOk.Position = _center + new Vector2(ox, 38);
        if (_btCancel != null) _btCancel.Position = _center + new Vector2(44, 38);
    }

    public override void Update(GameTime gameTime)
    {
        if (!IsVisible || _autoDismiss <= 0f) return;
        _autoDismiss -= (float)gameTime.ElapsedGameTime.TotalSeconds;
        if (_autoDismiss <= 0f)
        {
            _autoDismiss = 0f;
            IsVisible = false;
            OnOk?.Invoke();
        }
    }

    // Inner text area; the fallback box adds horizontal padding around this.
    private const int InnerWidth = 280;
    private const int PadX = 12;

    public override void Draw(SpriteBatch sb, Texture2D white)
    {
        if (!IsVisible) return;

        // Wrap the message to the dialog's inner width so a long notice does not
        // overflow the bar horizontally; the fallback box then grows to fit.
        var lines = _font != null ? WrapLines(_font, _message, InnerWidth) : new List<string>();
        var lineH = _font?.LineHeight ?? 0;
        var textBlockH = lines.Count * lineH;

        if (_background != null)
        {
            _background.Draw(sb, _center);
        }
        else
        {
            var boxW = InnerWidth + PadX * 2;
            var boxH = Math.Max(110, textBlockH + 80);
            var rect = new Rectangle((int)_center.X - boxW / 2, (int)_center.Y - boxH / 2, boxW, boxH);
            sb.Draw(white, rect, new Color(0, 0, 0, 210));
            DrawBorder(sb, white, rect, new Color(120, 100, 60));
        }

        if (_font != null)
        {
            // Centre the wrapped block vertically just above the middle, leaving the
            // lower part of the dialog for the OK/Cancel buttons (at _center.Y + 38).
            var startY = _center.Y - 8 - textBlockH / 2f;
            for (var i = 0; i < lines.Count; i++)
            {
                var w = _font.Measure(lines[i]).X;
                _font.Draw(sb, lines[i], new Vector2(_center.X - w / 2f, startY + i * lineH), Color.White);
            }
        }

        _btOk?.Draw(sb);
        if (_type == NoticeType.OkCancel) _btCancel?.Draw(sb);
    }

    /// <summary>Greedy word-wrap: splits <paramref name="text"/> into lines whose
    /// rendered width stays within <paramref name="maxWidth"/>. Honours explicit
    /// newlines; a single word wider than the limit overflows on its own line.</summary>
    private static List<string> WrapLines(BuiltInFont font, string text, float maxWidth)
    {
        var lines = new List<string>();
        if (string.IsNullOrEmpty(text)) return lines;

        foreach (var paragraph in text.Split('\n'))
        {
            var current = new StringBuilder();
            foreach (var word in paragraph.Split(' '))
            {
                if (current.Length == 0)
                {
                    current.Append(word);
                    continue;
                }
                var candidate = $"{current} {word}";
                if (font.Measure(candidate).X > maxWidth)
                {
                    lines.Add(current.ToString());
                    current.Clear();
                    current.Append(word);
                }
                else
                {
                    current.Clear();
                    current.Append(candidate);
                }
            }
            lines.Add(current.ToString());
        }
        return lines;
    }

    public override bool HandleMouseButton(int x, int y, bool down)
    {
        if (!IsVisible) return false;
        if (_btOk?.HandleMouseButton(x, y, down) == true) return true;
        if (_type == NoticeType.OkCancel && _btCancel?.HandleMouseButton(x, y, down) == true) return true;
        return true;
    }

    public override bool OnKeyPress(Keys key)
    {
        if (!IsVisible) return false;
        if (key == Keys.Enter || key == Keys.Escape)
        {
            IsVisible = false;
            (key == Keys.Enter ? OnOk : OnCancel)?.Invoke();
            return true;
        }
        return true;
    }

    private static void DrawBorder(SpriteBatch sb, Texture2D white, Rectangle rect, Color color)
    {
        sb.Draw(white, new Rectangle(rect.X, rect.Y, rect.Width, 1), color);
        sb.Draw(white, new Rectangle(rect.X, rect.Bottom - 1, rect.Width, 1), color);
        sb.Draw(white, new Rectangle(rect.X, rect.Y, 1, rect.Height), color);
        sb.Draw(white, new Rectangle(rect.Right - 1, rect.Y, 1, rect.Height), color);
    }
}
