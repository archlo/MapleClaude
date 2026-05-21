using MapleClaude.Render;
using MapleClaude.Wz;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace MapleClaude.UI;

/// <summary>
/// "Connecting to server…" modal drawn on top of LoginStage while the client
/// waits for a login response. BtCancel dismisses it (or the caller can hide it).
/// WZ: <c>Login.img/LoginWait/</c>
/// </summary>
public sealed class LoginWaitOverlay : Overlay
{
    private readonly WzSprite? _background;
    private readonly Button? _btCancel;
    private readonly BuiltInFont? _font;
    private readonly Vector2 _center;

    public Action? OnCancel { get; set; }
    public Action? OnDone { get; set; }

    private float _autoTimer = -1f;

    /// <summary>Show the overlay then call <paramref name="onDone"/> after <paramref name="seconds"/> seconds.</summary>
    public void ShowAndThen(float seconds, Action onDone)
    {
        IsVisible = true;
        _autoTimer = seconds;
        OnDone = onDone;
    }

    public override void Update(GameTime gameTime)
    {
        if (!IsVisible || _autoTimer < 0f) return;
        _autoTimer -= (float)gameTime.ElapsedGameTime.TotalSeconds;
        if (_autoTimer <= 0f)
        {
            _autoTimer = -1f;
            IsVisible = false;
            OnDone?.Invoke();
        }
    }

    public LoginWaitOverlay(WzTextureLoader loader, WzPackage? ui, BuiltInFont? font, Vector2 center)
    {
        _font = font;
        _center = center;

        var wait = ui?.GetItem("Login.img/LoginWait") as WzProperty;
        _background = wait?.Get("backgrnd") is WzCanvas bg ? loader.Load(bg) : null;

        var cancelRoot = wait?.Get("BtCancel") as WzProperty;
        if (cancelRoot != null)
        {
            _btCancel = new Button(loader, cancelRoot) { OnClick = () => OnCancel?.Invoke() };
        }

        if (_btCancel != null)
        {
            _btCancel.Position = center + new Vector2(0, 40);
        }
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
            var rect = new Rectangle((int)_center.X - 130, (int)_center.Y - 50, 260, 100);
            sb.Draw(white, rect, new Color(0, 0, 0, 200));
            var border = new Rectangle(rect.X - 1, rect.Y - 1, rect.Width + 2, rect.Height + 2);
            DrawBorder(sb, white, border, new Color(100, 100, 120));
        }

        if (_font != null)
        {
            var msg = "Connecting...";
            var sz = _font.Measure(msg);
            _font.Draw(sb, msg, _center + new Vector2(-sz.X / 2f, -20), Color.White);
        }
        _btCancel?.Draw(sb);
    }

    public override bool HandleMouseButton(int x, int y, bool down)
    {
        if (!IsVisible) return false;
        _btCancel?.HandleMouseButton(x, y, down);
        return true;
    }

    public override bool OnKeyPress(Keys key)
    {
        if (!IsVisible) return false;
        if (key == Keys.Escape) { OnCancel?.Invoke(); return true; }
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
