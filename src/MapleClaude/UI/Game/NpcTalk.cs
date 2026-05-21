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
    public enum DialogType { Ok, YesNo, Next, PrevNext }

    private readonly WzSprite? _background;
    private readonly Button? _btOk;
    private readonly Button? _btYes;
    private readonly Button? _btNo;
    private readonly Button? _btNext;
    private readonly Button? _btPrev;
    private readonly BuiltInFont? _font;
    private readonly List<Button> _allButtons = new();

    private string _text = string.Empty;
    private DialogType _dialogType = DialogType.Ok;

    public Action? OnOk { get; set; }
    public Action? OnYes { get; set; }
    public Action? OnNo { get; set; }
    public Action? OnNext { get; set; }
    public Action? OnPrev { get; set; }

    public NpcTalk(WzTextureLoader loader, WzPackage? ui, BuiltInFont? font)
    {
        _font = font;
        IsVisible = false;
        Position = new Vector2(170, 380);

        var npc = ui?.GetItem("UIWindow.img/NpcTalk") as WzProperty;
        _background = npc?.Get("backgrnd") is WzCanvas bc ? loader.Load(bc) : null;

        _btOk = MakeButton(loader, npc, "BtOK", () => { IsVisible = false; OnOk?.Invoke(); });
        _btYes = MakeButton(loader, npc, "BtYes", () => { IsVisible = false; OnYes?.Invoke(); });
        _btNo = MakeButton(loader, npc, "BtNo", () => { IsVisible = false; OnNo?.Invoke(); });
        _btNext = MakeButton(loader, npc, "BtNext", () => OnNext?.Invoke());
        _btPrev = MakeButton(loader, npc, "BtPrev", () => OnPrev?.Invoke());

        ApplyLayout();
    }

    public void Show(string text, DialogType type = DialogType.Ok)
    {
        _text = text;
        _dialogType = type;
        IsVisible = true;
    }

    private void ApplyLayout()
    {
        if (_btOk != null) _btOk.Position = Position + new Vector2(430, 105);
        if (_btYes != null) _btYes.Position = Position + new Vector2(390, 105);
        if (_btNo != null) _btNo.Position = Position + new Vector2(430, 105);
        if (_btNext != null) _btNext.Position = Position + new Vector2(430, 105);
        if (_btPrev != null) _btPrev.Position = Position + new Vector2(390, 105);
    }

    public override void Update(GameTime gameTime) => ApplyLayout();

    public override void Draw(SpriteBatch sb, Texture2D white)
    {
        if (!IsVisible) return;

        if (_background != null)
            _background.Draw(sb, Position + new Vector2(234, 65));
        else
        {
            sb.Draw(white, new Rectangle((int)Position.X, (int)Position.Y, 470, 130), new Color(15, 15, 25, 230));
            DrawBorder(sb, white, new Rectangle((int)Position.X, (int)Position.Y, 470, 130));
        }

        _font?.Draw(sb, _text, Position + new Vector2(70, 12), Color.White);

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
        }
    }

    public override bool HandleMouseButton(int x, int y, bool down)
    {
        if (!IsVisible) return false;
        foreach (var b in _allButtons)
            if (b.HandleMouseButton(x, y, down)) return true;
        return new Rectangle((int)Position.X, (int)Position.Y, 470, 130).Contains(x, y);
    }

    public override bool OnKeyPress(Keys key)
    {
        if (!IsVisible) return false;
        if (key == Keys.Enter) { IsVisible = false; OnOk?.Invoke(); return true; }
        return true;
    }

    private Button? MakeButton(WzTextureLoader loader, WzProperty? root, string name, Action onClick)
    {
        var pr = root?.Get(name) as WzProperty;
        if (pr is null) return null;
        var b = new Button(loader, pr) { OnClick = onClick };
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
