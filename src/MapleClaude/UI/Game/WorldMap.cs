using MapleClaude.Render;
using MapleClaude.UI;
using MapleClaude.Wz;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace MapleClaude.UI.Game;

/// <summary>
/// World map overlay. Toggle with W key (or via menu).
/// Shows a zoomable/draggable world map image with region hotspots,
/// current-position marker, and NPC/portal markers.
/// WZ: UIWindow2.img/WorldMap/ (or MapHelper.img)
///   BaseImg     — the full world map bitmap
///   MapHelper.img/worldmap/worldmap → used for region spot definitions
/// </summary>
public sealed class WorldMap : GamePanel
{
    // ── Layout ────────────────────────────────────────────────────────────────
    private const int PanelW = 640;
    private const int PanelH = 480;

    // ── WZ sprites ────────────────────────────────────────────────────────────
    private readonly WzSprite? _mapBase;
    private readonly WzSprite? _mapSearch;
    private readonly WzSprite? _marker;     // current-position arrow
    private readonly WzSprite? _npcDot;

    // ── Buttons ───────────────────────────────────────────────────────────────
    private readonly Button? _btClose;
    private readonly Button? _btSearch;
    private readonly List<Button> _allButtons = new();

    // ── Region hotspots ───────────────────────────────────────────────────────
    private sealed class MapRegion
    {
        public string Name;
        public Rectangle Bounds;      // within the map image
        public int MapId;
        public MapRegion(string n, Rectangle b, int id) { Name = n; Bounds = b; MapId = id; }
    }

    private static readonly MapRegion[] Regions =
    [
        new("Maple Island",    new Rectangle(340, 310, 80,  60),  1000000),
        new("Victoria Island", new Rectangle(240, 200, 160, 130), 100000000),
        new("Ossyria",         new Rectangle(80,  60,  160, 140), 200000000),
        new("Ludus Lake",      new Rectangle(350, 80,  90,  80),  220000000),
        new("Minar Forest",    new Rectangle(440, 200, 80,  70),  240000000),
        new("Mulung",          new Rectangle(460, 90,  60,  60),  250000000),
        new("Nihal Desert",    new Rectangle(40,  250, 100, 80),  260000000),
        new("Temple of Time",  new Rectangle(520, 300, 60,  50),  270000000),
        new("Masteria",        new Rectangle(140, 310, 90,  80),  600000000),
    ];

    // ── State ─────────────────────────────────────────────────────────────────
    private Vector2 _mapOffset = Vector2.Zero;   // scroll offset within panel
    private bool _dragging;
    private Vector2 _dragStart;
    private MapRegion? _hoveredRegion;
    private string _currentMapName = "Victoria Island";
    private int _currentMapId = 100000000;

    // Search
    private string _searchText = string.Empty;
    private bool _searchMode;

    private readonly BuiltInFont? _font;

    public WorldMap(WzTextureLoader loader, WzPackage? ui, BuiltInFont? font)
    {
        _font = font;
        IsVisible = false;
        Position = new Vector2(80, 60);

        // WZ: UIWindow2.img/WorldMap  or  UIWindow.img/WorldMap
        var wm = ui?.GetItem("UIWindow2.img/WorldMap") as WzProperty
              ?? ui?.GetItem("UIWindow.img/WorldMap") as WzProperty;

        _mapBase   = wm?.Get("BaseImg")  is WzCanvas bc ? loader.Load(bc) : null;
        _mapSearch = wm?.Get("BackgrndSearch") is WzCanvas sc ? loader.Load(sc) : null;
        _marker    = wm?.Get("MarkerYou") is WzCanvas mc ? loader.Load(mc) : null;
        _npcDot    = wm?.Get("MarkerNpc") is WzCanvas nc ? loader.Load(nc) : null;

        _btClose  = MakeBtn(loader, wm, "BtClose",       () => IsVisible = false);
        _btSearch = MakeBtn(loader, wm, "BtSearch",      () => _searchMode = !_searchMode);

        LayoutButtons();
    }

    public void SetCurrentMap(int mapId, string mapName)
    {
        _currentMapId   = mapId;
        _currentMapName = mapName;
    }

    // ── Update ────────────────────────────────────────────────────────────────

    public override void Update(GameTime gt)
    {
        LayoutButtons();
        var mouse = Mouse.GetState();
        var mp = new Vector2(mouse.X, mouse.Y);

        if (_dragging)
        {
            if (mouse.LeftButton == ButtonState.Pressed)
            {
                _mapOffset += mp - _dragStart;
                _dragStart = mp;
                ClampMapOffset();
            }
            else _dragging = false;
        }

        // Detect hovered region
        _hoveredRegion = null;
        var mapAreaPos = Position + new Vector2(8, 40);
        foreach (var r in Regions)
        {
            var sr = new Rectangle(
                (int)(mapAreaPos.X + r.Bounds.X + _mapOffset.X),
                (int)(mapAreaPos.Y + r.Bounds.Y + _mapOffset.Y),
                r.Bounds.Width, r.Bounds.Height);
            if (sr.Contains((int)mp.X, (int)mp.Y)) { _hoveredRegion = r; break; }
        }
    }

    // ── Draw ──────────────────────────────────────────────────────────────────

    public override void Draw(SpriteBatch sb, Texture2D white)
    {
        if (!IsVisible) return;

        var px = (int)Position.X;
        var py = (int)Position.Y;

        // Panel background
        if (_mapBase != null)
            _mapBase.Draw(sb, Position + new Vector2(PanelW / 2f, PanelH / 2f));
        else
        {
            sb.Draw(white, new Rectangle(px, py, PanelW, PanelH), new Color(10, 15, 30));
            DrawBorder(sb, white, new Rectangle(px, py, PanelW, PanelH), new Color(60, 70, 110));
        }

        // Title strip
        sb.Draw(white, new Rectangle(px, py, PanelW, 36), new Color(15, 18, 40, 220));
        _font?.Draw(sb, "World Map", new Vector2(px + 260, py + 10), new Color(220, 200, 140));
        _font?.Draw(sb, $"Current: {_currentMapName}", new Vector2(px + 8, py + 10), new Color(180, 200, 220));

        // Map area (scrollable)
        var mapAreaX = px + 8;
        var mapAreaY = py + 40;
        var mapAreaW = PanelW - 16;
        var mapAreaH = PanelH - 80;

        // Clip to map area using scissor-like darkening border
        sb.Draw(white, new Rectangle(mapAreaX, mapAreaY, mapAreaW, mapAreaH), new Color(20, 30, 20));

        // Draw regions
        foreach (var region in Regions)
        {
            var rx = mapAreaX + region.Bounds.X + (int)_mapOffset.X;
            var ry = mapAreaY + region.Bounds.Y + (int)_mapOffset.Y;
            var rr = new Rectangle(rx, ry, region.Bounds.Width, region.Bounds.Height);

            // Clip to panel
            if (!new Rectangle(mapAreaX, mapAreaY, mapAreaW, mapAreaH).Intersects(rr)) continue;

            var isHover   = region == _hoveredRegion;
            var isCurrent = region.MapId == _currentMapId || _currentMapId >= region.MapId
                             && _currentMapId < region.MapId + 10000000;

            var fillColor = isCurrent ? new Color(60, 100, 60, 180)
                          : isHover   ? new Color(60, 80, 120, 180)
                          : new Color(30, 50, 30, 120);
            var borderColor = isCurrent ? new Color(100, 200, 100)
                            : isHover   ? new Color(120, 160, 220)
                            : new Color(60, 90, 60);

            sb.Draw(white, rr, fillColor);
            DrawBorder(sb, white, rr, borderColor);
            if (_font != null)
            {
                var sz = _font.Measure(region.Name);
                var tx = rx + (region.Bounds.Width - (int)sz.X) / 2;
                var tyy = ry + (region.Bounds.Height - _font.LineHeight) / 2;
                _font.Draw(sb, region.Name, new Vector2(tx, tyy),
                    isCurrent ? Color.White : new Color(200, 210, 200));
            }
        }

        // Current position marker
        var curRegion = FindRegionForMap(_currentMapId);
        if (curRegion != null)
        {
            var mx = mapAreaX + curRegion.Bounds.X + curRegion.Bounds.Width / 2 + (int)_mapOffset.X;
            var my = mapAreaY + curRegion.Bounds.Y + curRegion.Bounds.Height / 2 + (int)_mapOffset.Y;
            if (_marker != null)
                _marker.Draw(sb, new Vector2(mx, my));
            else
            {
                sb.Draw(white, new Rectangle(mx - 4, my - 4, 8, 8), Color.Yellow);
                sb.Draw(white, new Rectangle(mx - 2, my - 8, 4, 4), Color.Yellow);
            }
        }

        // Tooltip for hovered region
        if (_hoveredRegion != null && _font != null)
        {
            var ttText = _hoveredRegion.Name;
            var ttSz   = _font.Measure(ttText);
            var mouse  = Mouse.GetState();
            var ttR    = new Rectangle(mouse.X + 10, mouse.Y - 20, (int)ttSz.X + 8, _font.LineHeight + 4);
            sb.Draw(white, ttR, new Color(0, 0, 0, 200));
            DrawBorder(sb, white, ttR, new Color(100, 100, 160));
            _font.Draw(sb, ttText, new Vector2(ttR.X + 4, ttR.Y + 2), Color.White);
        }

        // Search bar (if active)
        if (_searchMode)
        {
            var srX = px + 8;
            var srY = py + PanelH - 40;
            sb.Draw(white, new Rectangle(srX, srY, 300, 24), new Color(20, 20, 40));
            DrawBorder(sb, white, new Rectangle(srX, srY, 300, 24), new Color(80, 80, 130));
            _font?.Draw(sb, $"Search: {_searchText}_", new Vector2(srX + 4, srY + 5), Color.White);
        }

        foreach (var b in _allButtons) b.Draw(sb);
    }

    // ── Input ─────────────────────────────────────────────────────────────────

    public override bool HandleMouseButton(int x, int y, bool down)
    {
        if (!IsVisible) return false;

        foreach (var b in _allButtons)
            if (b.HandleMouseButton(x, y, down)) return true;

        var panelRect = new Rectangle((int)Position.X, (int)Position.Y, PanelW, PanelH);
        if (!panelRect.Contains(x, y)) return false;

        if (down)
        {
            // Click on hovered region — log navigation intent
            if (_hoveredRegion != null)
            {
                // Will be wired to map-change packet later
            }
            _dragging   = true;
            _dragStart  = new Vector2(x, y);
        }
        return true;
    }

    public override bool OnKeyPress(Keys key)
    {
        if (!IsVisible) return false;
        if (key == Keys.Escape) { IsVisible = false; return true; }
        if (_searchMode)
        {
            if (key == Keys.Back && _searchText.Length > 0)
                _searchText = _searchText[..^1];
            return true;
        }
        return false;
    }

    public override void OnTextInput(char ch)
    {
        if (_searchMode && ch >= ' ' && ch < 127 && _searchText.Length < 32)
            _searchText += ch;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private MapRegion? FindRegionForMap(int mapId)
    {
        foreach (var r in Regions)
            if (mapId >= r.MapId && mapId < r.MapId + 10_000_000) return r;
        return null;
    }

    private void ClampMapOffset()
    {
        var maxX = 100f; var minX = -(640f - (PanelW - 16));
        var maxY = 100f; var minY = -(480f - (PanelH - 80));
        _mapOffset = new Vector2(
            Math.Clamp(_mapOffset.X, minX, maxX),
            Math.Clamp(_mapOffset.Y, minY, maxY));
    }

    private void LayoutButtons()
    {
        if (_btClose  != null) _btClose.Position  = Position + new Vector2(PanelW - 20, 6);
        if (_btSearch != null) _btSearch.Position = Position + new Vector2(PanelW - 50, PanelH - 32);
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
