using MapleClaude.Render;
using MapleClaude.Wz;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace MapleClaude.UI;

/// <summary>
/// Modal "system notice" that reuses the v95 client's pre-baked notice art (mirrors CUINotice): a
/// <c>Login.img/Notice/backgrnd/0</c> panel (249×142), a baked message canvas
/// <c>Login.img/Notice/text/{id}</c>, and a clickable <c>Login.img/Notice/BtYes</c> OK button. The
/// message text is part of the WZ asset — we never render our own label. Used for the "you must
/// register a PIC" prompt (text id 95) shown before the PIC pad on first secondary-password setup.
/// </summary>
public sealed class SystemNoticeOverlay : Overlay
{
    private const int BgW = 249, BgH = 142;

    private readonly WzTextureLoader _loader;
    private readonly WzPackage? _ui;
    private readonly Vector2 _center;
    private readonly WzSprite? _bg;
    private readonly Button? _btYes;

    private WzSprite? _text;     // per-message baked text canvas (Notice/text/{id}); set in Show
    private Action? _onOk;

    public SystemNoticeOverlay(WzTextureLoader loader, WzPackage? ui, Vector2 center)
    {
        _loader = loader;
        _ui = ui;
        _center = center;

        var notice = ui?.GetItem("Login.img/Notice") as WzProperty;
        if ((notice?.Get("backgrnd") as WzProperty)?.Get("0") is WzCanvas bg)
        {
            _bg = loader.Load(bg);
        }
        if (notice?.Get("BtYes") is WzProperty yes)
        {
            _btYes = new Button(loader, yes) { OnClick = Confirm };
        }
    }

    /// <summary>Show the notice carrying baked message <c>Notice/text/{textId}</c>;
    /// <paramref name="onOk"/> fires when the player clicks OK (or presses Enter/Esc).</summary>
    public void Show(int textId, Action onOk)
    {
        _onOk = onOk;
        _text = _ui?.GetItem($"Login.img/Notice/text/{textId}") is WzCanvas c ? _loader.Load(c) : null;

        var bgTL = _center - new Vector2(BgW / 2f, BgH / 2f);
        // BtYes (50×23) centred along the bottom of the 249×142 panel.
        if (_btYes != null) _btYes.Position = bgTL + new Vector2(100, 107);
        IsVisible = true;
    }

    private void Confirm()
    {
        IsVisible = false;
        _onOk?.Invoke();
    }

    public override void Update(GameTime gameTime)
    {
        if (!IsVisible) return;
        var ms = Mouse.GetState();
        _btYes?.Update(ms.X, ms.Y, ms.LeftButton == ButtonState.Pressed);
    }

    public override void Draw(SpriteBatch sb, Texture2D white)
    {
        if (!IsVisible) return;
        sb.Draw(white, sb.GraphicsDevice.PresentationParameters.Bounds, new Color(0, 0, 0, 150));

        var bgTL = _center - new Vector2(BgW / 2f, BgH / 2f);
        if (_bg != null)
        {
            _bg.Draw(sb, bgTL);
        }
        else
        {
            sb.Draw(white, new Rectangle((int)bgTL.X, (int)bgTL.Y, BgW, BgH), new Color(40, 40, 48));
        }
        // The text canvas already carries the message; centre it horizontally near the top.
        if (_text != null)
        {
            _text.Draw(sb, bgTL + new Vector2((BgW - _text.Width) / 2f, 14));
        }
        _btYes?.Draw(sb);
    }

    public override bool HandleMouseButton(int x, int y, bool down)
    {
        if (!IsVisible) return false;
        if (_btYes?.HandleMouseButton(x, y, down) == true) return true;
        return true; // swallow clicks while the modal is up
    }

    public override bool OnKeyPress(Keys key)
    {
        if (!IsVisible) return false;
        if (key == Keys.Enter || key == Keys.Escape) { Confirm(); return true; }
        return true;
    }
}
