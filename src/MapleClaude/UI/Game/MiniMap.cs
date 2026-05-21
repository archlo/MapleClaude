using MapleClaude.Map;
using MapleClaude.Render;
using MapleClaude.UI;
using MapleClaude.Wz;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace MapleClaude.UI.Game;

/// <summary>
/// Mini-map panel. Toggle with M.
/// Modes: MAX (full 260×200 with map + dots) / MIN (compact name strip).
/// Shows player arrow, NPC dots, portal dots in world-to-map coordinate space.
///
/// WZ: UIWindow2.img/MiniMap/
///   MAX      — expanded frame background
///   MIN      — compact strip background
///   BtMin/Max/Big/Npc — control buttons
///   MiniMapSimpleOff / MiniMapSimpleOn — map area backgrounds
/// </summary>
public sealed class MiniMap : GamePanel
{
    // ── WZ sprites ─────────────────────────────────────────────────────────
    private readonly WzSprite? _bgMax;
    private readonly WzSprite? _bgMin;
    private readonly WzSprite? _playerArrow;

    // ── Buttons ─────────────────────────────────────────────────────────────
    private readonly Button? _btMin;
    private readonly Button? _btMax;
    private readonly Button? _btBig;
    private readonly Button? _btNpc;
    private readonly List<Button> _allButtons = new();

    // ── State ────────────────────────────────────────────────────────────────
    private bool _expanded = true;
    private bool _showNpcs = true;
    private string _mapName   = "Maple Island";
    private string _streetName = "Maple Road";

    // ── Map bounds for coordinate mapping ───────────────────────────────────
    private Rectangle _mapBounds = new Rectangle(-2000, -2000, 4000, 4000);

    // ── Player position in world space (updated by GameStage each frame) ────
    public Vector2 PlayerWorldPos { get; set; }

    // ── NPC world positions (updated by GameStage) ──────────────────────────
    private readonly List<(Vector2 pos, Color dot)> _mapDots = new();

    // ── Panel geometry ───────────────────────────────────────────────────────
    private const int PanelW    = 258;
    private const int PanelH    = 198;
    private const int MapAreaX  = 4;
    private const int MapAreaY  = 32;
    private const int MapAreaW  = 250;
    private const int MapAreaH  = 160;
    private const int MinH      = 20;

    // ── Drag ─────────────────────────────────────────────────────────────────
    private bool _dragging;
    private Vector2 _dragOffset;

    private readonly BuiltInFont? _font;

    public MiniMap(WzTextureLoader loader, WzPackage? ui, BuiltInFont? font)
    {
        _font = font;
        IsVisible = true;
        Position = new Vector2(4, 4);

        var mm = ui?.GetItem("UIWindow2.img/MiniMap") as WzProperty;
        _bgMax       = mm?.Get("MAX") is WzCanvas mc ? loader.Load(mc) : null;
        _bgMin       = mm?.Get("MIN") is WzCanvas nc ? loader.Load(nc) : null;
        _playerArrow = mm?.Get("MiniMapSimpleOff") is WzCanvas ac ? loader.Load(ac) : null;

        _btMin = MakeBtn(loader, mm, "BtMin", () => _expanded = false);
        _btMax = MakeBtn(loader, mm, "BtMax", () => _expanded = true);
        _btBig = MakeBtn(loader, mm, "BtBig", () => { });
        _btNpc = MakeBtn(loader, mm, "BtNpc", () => _showNpcs = !_showNpcs);

        LayoutButtons();
    }

    public void SetMapInfo(string street, string name, Rectangle mapBounds)
    {
        _streetName = street;
        _mapName    = name;
        _mapBounds  = mapBounds;
    }

    public void SetDots(IEnumerable<(Vector2 pos, Color dot)> dots)
    {
        _mapDots.Clear();
        _mapDots.AddRange(dots);
    }

    // ── Update ───────────────────────────────────────────────────────────────

    public override void Update(GameTime gameTime)
    {
        LayoutButtons();

        // Drag
        var mouse = Mouse.GetState();
        var mp    = new Vector2(mouse.X, mouse.Y);
        if (_dragging)
        {
            if (mouse.LeftButton == ButtonState.Pressed)
                Position = mp - _dragOffset;
            else
                _dragging = false;
        }
    }

    // ── Draw ─────────────────────────────────────────────────────────────────

    public override void Draw(SpriteBatch sb, Texture2D white)
    {
        if (!IsVisible) return;

        var px = (int)Position.X;
        var py = (int)Position.Y;

        if (_expanded)
        {
            // Frame background
            if (_bgMax != null)
                _bgMax.Draw(sb, Position + new Vector2(PanelW / 2f, PanelH / 2f));
            else
            {
                sb.Draw(white, new Rectangle(px, py, PanelW, PanelH), new Color(0, 0, 0, 200));
                DrawBorder(sb, white, new Rectangle(px, py, PanelW, PanelH), new Color(80, 80, 110));
            }

            // Title strip
            sb.Draw(white, new Rectangle(px, py, PanelW, 28), new Color(10, 10, 20, 220));
            _font?.Draw(sb, _streetName, new Vector2(px + 4, py + 2),  new Color(180, 180, 140));
            _font?.Draw(sb, _mapName,    new Vector2(px + 4, py + 14), Color.White);

            // Map area background
            var mapRect = new Rectangle(px + MapAreaX, py + MapAreaY, MapAreaW, MapAreaH);
            sb.Draw(white, mapRect, new Color(20, 40, 20, 180));

            // NPC and portal dots
            if (_showNpcs)
            {
                foreach (var (wpos, color) in _mapDots)
                {
                    var sp = WorldToMapArea(wpos, mapRect);
                    if (mapRect.Contains(sp))
                        sb.Draw(white, new Rectangle(sp.X - 2, sp.Y - 2, 5, 5), color);
                }
            }

            // Player arrow
            var playerSp = WorldToMapArea(PlayerWorldPos, mapRect);
            if (_playerArrow != null)
                _playerArrow.Draw(sb, new Vector2(playerSp.X, playerSp.Y));
            else
            {
                // Small yellow arrow triangle
                sb.Draw(white, new Rectangle(playerSp.X - 3, playerSp.Y - 3, 6, 6), Color.Yellow);
            }
        }
        else
        {
            // Compact strip
            if (_bgMin != null)
                _bgMin.Draw(sb, Position + new Vector2(70, 9));
            else
                sb.Draw(white, new Rectangle(px, py, 140, 18), new Color(0, 0, 0, 200));

            _font?.Draw(sb, _mapName, new Vector2(px + 4, py + 2), Color.White);
        }

        foreach (var b in _allButtons) b.Draw(sb);
    }

    // ── Input ────────────────────────────────────────────────────────────────

    public override bool HandleMouseButton(int x, int y, bool down)
    {
        if (!IsVisible) return false;

        foreach (var b in _allButtons)
            if (b.HandleMouseButton(x, y, down)) return true;

        var titleRect = new Rectangle((int)Position.X, (int)Position.Y, PanelW, 28);
        if (down && titleRect.Contains(x, y))
        {
            _dragging   = true;
            _dragOffset = new Vector2(x - Position.X, y - Position.Y);
            return true;
        }

        var panelRect = new Rectangle((int)Position.X, (int)Position.Y,
            PanelW, _expanded ? PanelH : MinH);
        return panelRect.Contains(x, y);
    }

    public override bool OnKeyPress(Keys key)
    {
        if (key == Keys.Escape && IsVisible) { IsVisible = false; return true; }
        return false;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>Maps a world coordinate to a pixel inside the map area rectangle.</summary>
    private Point WorldToMapArea(Vector2 world, Rectangle mapRect)
    {
        var fx = (world.X - _mapBounds.X) / (float)_mapBounds.Width;
        var fy = (world.Y - _mapBounds.Y) / (float)_mapBounds.Height;
        return new Point(
            mapRect.X + (int)(fx * mapRect.Width),
            mapRect.Y + (int)(fy * mapRect.Height));
    }

    private void LayoutButtons()
    {
        var bx = Position.X + PanelW - 80;
        var by = Position.Y - 2;
        if (_btMin != null) _btMin.Position = new Vector2(bx,      by);
        if (_btMax != null) _btMax.Position = new Vector2(bx + 14, by);
        if (_btBig != null) _btBig.Position = new Vector2(bx + 28, by);
        if (_btNpc != null) _btNpc.Position = new Vector2(bx + 56, by);
    }

    private Button? MakeBtn(WzTextureLoader loader, WzProperty? root, string name, Action onClick)
    {
        var pr = root?.Get(name) as WzProperty;
        if (pr is null) return null;
        var b = new Button(loader, pr) { OnClick = onClick };
        _allButtons.Add(b);
        return b;
    }

    private static void DrawBorder(SpriteBatch sb, Texture2D white, Rectangle r, Color c)
    {
        sb.Draw(white, new Rectangle(r.X, r.Y, r.Width, 1), c);
        sb.Draw(white, new Rectangle(r.X, r.Bottom - 1, r.Width, 1), c);
        sb.Draw(white, new Rectangle(r.X, r.Y, 1, r.Height), c);
        sb.Draw(white, new Rectangle(r.Right - 1, r.Y, 1, r.Height), c);
    }
}
