using MapleClaude.Render;
using MapleClaude.Wz;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace MapleClaude.UI.Game;

/// <summary>
/// QuickSlot key-config sub-panel — the v95 <c>CQuickslotKeyModifyDlg</c> opened by
/// KeyConfig's <c>BtQuickSlot</c>. Renders <c>UIWindow.img/KeyConfig/quickslotConfig</c>
/// (a self-contained 266×238 mini-keyboard with its key glyphs baked into
/// <c>backgrnd</c>) plus the <c>BtQuickSetting</c> toggle.
///
/// The interactive quickslot-key remapping (the CQuickslotKeyMappedMan model, the
/// quickslot bar on the status bar, and the <c>QuickslotKeyMappedModified</c> save)
/// is a separate system from the func-key map; this panel renders the authentic
/// window and is the seam those pieces wire into next.
/// </summary>
public sealed class QuickSlotConfig : GamePanel
{
    private const int PanelW = 266;
    private const int PanelH = 238;

    private readonly WzSprite? _background;
    private readonly Button? _btSetting;
    private readonly BuiltInFont? _font;

    private bool _windowDrag;
    private Vector2 _windowDragOff;

    public QuickSlotConfig(WzTextureLoader loader, WzPackage? ui, BuiltInFont? font)
    {
        _font = font;
        IsVisible = false;
        Position = new Vector2(360, 250);

        var qs = ui?.GetItem("UIWindow.img/KeyConfig/quickslotConfig") as WzProperty;
        _background = qs?.Get("backgrnd") is WzCanvas bc ? loader.Load(bc) : null;
        if (qs?.Get("BtQuickSetting") is WzProperty st)
            _btSetting = new Button(loader, st) { OnClick = () => { } };
    }

    public void Open() { IsVisible = true; _windowDrag = false; }

    public override void Update(GameTime gt)
    {
        if (!IsVisible) return;
        if (_btSetting != null) _btSetting.Position = Position + new Vector2(PanelW - 40, PanelH - 36);

        var m = Mouse.GetState();
        if (_windowDrag)
        {
            if (m.LeftButton == ButtonState.Pressed) Position = new Vector2(m.X, m.Y) - _windowDragOff;
            else _windowDrag = false;
        }
    }

    public override void Draw(SpriteBatch sb, Texture2D white)
    {
        if (!IsVisible) return;
        var px = (int)Position.X;
        var py = (int)Position.Y;

        if (_background != null)
            _background.Draw(sb, Position + _background.Origin);
        else
        {
            sb.Draw(white, new Rectangle(px, py, PanelW, PanelH), new Color(14, 16, 28, 245));
            _font?.Draw(sb, "QuickSlot", new Vector2(px + 90, py + 6), new Color(220, 200, 150));
        }
        _btSetting?.Draw(sb);
    }

    public override bool HandleMouseButton(int x, int y, bool down)
    {
        if (!IsVisible) return false;
        var inside = new Rectangle((int)Position.X, (int)Position.Y, PanelW, PanelH).Contains(x, y);
        if (_btSetting != null && _btSetting.HandleMouseButton(x, y, down)) return true;
        if (!down) return inside;
        if (!inside) return false;

        // Drag the window from the title strip.
        if (y - (int)Position.Y < 24)
        {
            _windowDrag = true;
            _windowDragOff = new Vector2(x - Position.X, y - Position.Y);
        }
        return true;
    }

    public override bool OnKeyPress(Keys key)
    {
        if (!IsVisible) return false;
        if (key == Keys.Escape) { IsVisible = false; return true; }
        return false;
    }
}
