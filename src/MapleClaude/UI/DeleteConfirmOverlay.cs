using MapleClaude.Render;
using MapleClaude.Wz;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace MapleClaude.UI;

/// <summary>
/// Character-delete confirmation. The v95 server (Kinoko) validates the
/// account's secondary password before deleting (it returns IncorrectSPW
/// otherwise), so this prompts for it and hands it to <see cref="OnConfirm"/>
/// together with the character id. WZ: reuses <c>Login.img/Notice</c> assets.
/// </summary>
public sealed class DeleteConfirmOverlay : Overlay
{
    private readonly WzSprite? _background;
    private readonly Button? _btOk;
    private readonly Button? _btCancel;
    private readonly BuiltInFont? _font;
    private readonly TextField _spw;
    private readonly Vector2 _center;

    private string _charName = string.Empty;
    private int _charId = -1;

    /// <summary>(characterId, secondaryPassword) — the caller sends DeleteCharacter.</summary>
    public Action<int, string>? OnConfirm { get; set; }
    public Action? OnCancel { get; set; }

    public DeleteConfirmOverlay(WzTextureLoader loader, WzPackage? ui, BuiltInFont? font, Vector2 center)
    {
        _font = font;
        _center = center;

        var notice = ui?.GetItem("Login.img/Notice") as WzProperty;
        _background = notice?.Get("backgrnd") is WzCanvas bg ? loader.Load(bg) : null;

        var okRoot = notice?.Get("BtOK") as WzProperty;
        if (okRoot != null)
            _btOk = new Button(loader, okRoot) { OnClick = Confirm, Position = center + new Vector2(-44, 44) };
        var cancelRoot = notice?.Get("BtCancel") as WzProperty;
        if (cancelRoot != null)
            _btCancel = new Button(loader, cancelRoot) { OnClick = Cancel, Position = center + new Vector2(40, 44) };

        _spw = new TextField
        {
            Position = center + new Vector2(-80, 6),
            Width = 160,
            Height = 20,
            Font = font,
            IsPassword = true,
            MaxLength = 16,
        };
    }

    public void Show(string charName, int charId)
    {
        _charName = charName;
        _charId = charId;
        _spw.Clear();
        _spw.IsFocused = true;
        IsVisible = true;
    }

    private void Confirm()
    {
        IsVisible = false;
        OnConfirm?.Invoke(_charId, _spw.Text);
    }

    private void Cancel()
    {
        IsVisible = false;
        OnCancel?.Invoke();
    }

    public override void Update(GameTime gameTime)
    {
        if (IsVisible) _spw.Update(gameTime);
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
            var rect = new Rectangle((int)_center.X - 150, (int)_center.Y - 60, 300, 120);
            sb.Draw(white, rect, new Color(0, 0, 0, 215));
            DrawBorder(sb, white, rect, new Color(150, 80, 60));
        }

        if (_font != null)
        {
            var l1 = $"Delete {_charName}?";
            const string l2 = "Enter your 2nd password:";
            _font.Draw(sb, l1, _center + new Vector2(-_font.Measure(l1).X / 2f, -42), Color.White);
            _font.Draw(sb, l2, _center + new Vector2(-_font.Measure(l2).X / 2f, -24), new Color(220, 200, 120));
        }

        _spw.Draw(sb, white);
        _btOk?.Draw(sb);
        _btCancel?.Draw(sb);
    }

    public override bool HandleMouseButton(int x, int y, bool down)
    {
        if (!IsVisible) return false;
        _spw.HandleMouseButton(x, y, down);
        if (_btOk?.HandleMouseButton(x, y, down) == true) return true;
        if (_btCancel?.HandleMouseButton(x, y, down) == true) return true;
        return true;
    }

    public override bool OnKeyPress(Keys key)
    {
        if (!IsVisible) return false;
        if (key == Keys.Escape) { Cancel(); return true; }
        if (key == Keys.Enter) { Confirm(); return true; }
        _spw.OnKeyPress(key, Keyboard.GetState());
        return true;
    }

    public override void OnTextInput(char character)
    {
        if (IsVisible) _spw.OnTextInput(character);
    }

    private static void DrawBorder(SpriteBatch sb, Texture2D white, Rectangle rect, Color color)
    {
        sb.Draw(white, new Rectangle(rect.X, rect.Y, rect.Width, 1), color);
        sb.Draw(white, new Rectangle(rect.X, rect.Bottom - 1, rect.Width, 1), color);
        sb.Draw(white, new Rectangle(rect.X, rect.Y, 1, rect.Height), color);
        sb.Draw(white, new Rectangle(rect.Right - 1, rect.Y, 1, rect.Height), color);
    }
}
