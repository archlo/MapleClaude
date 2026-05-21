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

    public void Show(string message, NoticeType type = NoticeType.Ok)
    {
        _message = message;
        _type = type;
        IsVisible = true;

        var ox = _type == NoticeType.OkCancel ? -40f : 0f;
        if (_btOk != null) _btOk.Position = _center + new Vector2(ox, 38);
        if (_btCancel != null) _btCancel.Position = _center + new Vector2(44, 38);
    }

    public override void Draw(SpriteBatch sb, Texture2D white)
    {
        if (!IsVisible) return;

        if (_background != null)
        {
            _background.Draw(sb, _center);
        }
        else
        {
            var rect = new Rectangle((int)_center.X - 150, (int)_center.Y - 55, 300, 110);
            sb.Draw(white, rect, new Color(0, 0, 0, 210));
            DrawBorder(sb, white, rect, new Color(120, 100, 60));
        }

        if (_font != null)
        {
            var sz = _font.Measure(_message);
            _font.Draw(sb, _message, _center + new Vector2(-sz.X / 2f, -10), Color.White);
        }

        _btOk?.Draw(sb);
        if (_type == NoticeType.OkCancel) _btCancel?.Draw(sb);
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
