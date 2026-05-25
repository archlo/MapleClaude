using MapleClaude.Map;
using MapleClaude.Render;
using MapleClaude.UI;
using MapleClaude.Wz;
using Microsoft.Extensions.Logging;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace MapleClaude.UI.Game;

/// <summary>
/// Authentic v95 minimap, anchored top-left. Three states cycle with <c>M</c> and the
/// frame buttons: <see cref="Mode.Min"/> (collapsed name strip) →
/// <see cref="Mode.Normal"/> (framed map) → <see cref="Mode.Max"/> (2× framed map).
///
/// Renders the per-map bitmap (<see cref="MiniMapData.Canvas"/>) scrolled to follow the
/// player, with the real <c>Map.wz/MapHelper.img/minimap</c> icons for the player, other
/// players, NPCs and portals plotted via <see cref="MiniMapData.WorldToCanvas"/>. The frame
/// is the genuine 9-slice from <c>UI.wz/UIWindow2.img/MiniMap</c> (MinMap / MaxMap / Min).
/// </summary>
public sealed class MiniMap : GamePanel
{
    public enum Mode { Min = 0, Normal = 1, Max = 2 }

    // ── A 9-slice window frame (nw n ne / w c e / sw s se). ────────────────────
    private sealed class FrameSet
    {
        public WzSprite? Nw, N, Ne, W, C, E, Sw, S, Se;
        public int BorderL => W?.Width  ?? 9;
        public int BorderR => E?.Width  ?? 9;
        public int BorderB => S?.Height ?? 9;
        public int TitleH  => N?.Height ?? 21;   // top band == top-edge piece height
        public int NwW     => Nw?.Width ?? 64;
    }

    // ── WZ assets ──────────────────────────────────────────────────────────────
    private readonly FrameSet _minMap = new();   // Normal mode frame
    private readonly FrameSet _maxMap = new();    // Max mode frame
    private readonly WzSprite? _stripW, _stripC, _stripE;   // collapsed strip pieces
    private readonly MiniMapMarkers _markers;

    private readonly Button? _btMin;
    private readonly Button? _btMax;
    private readonly Button? _btMap;
    private readonly List<Button> _buttons = new();

    private readonly BuiltInFont? _font;
    private readonly ILogger? _logger;

    // ── State ────────────────────────────────────────────────────────────────
    private Mode _mode = Mode.Normal;
    private string _mapName   = string.Empty;
    private string _streetName = string.Empty;
    private MiniMapData? _data;

    // Per-frame entity feed (world coordinates).
    public Vector2 PlayerWorldPos { get; set; }
    private readonly List<Vector2> _npcs    = new();
    private readonly List<Vector2> _others  = new();
    private readonly List<Vector2> _portals = new();

    // ── Layout constants ───────────────────────────────────────────────────────
    private const int NormalPaneCapW = 200, NormalPaneCapH = 140;
    private const int MaxPaneCapW    = 420, MaxPaneCapH    = 280;
    private const int StripH         = 20;
    private const int BtnGap         = 1;
    private const int TitlePadRight  = 7;

    public MiniMap(WzTextureLoader loader, WzPackage? ui, BuiltInFont? font,
                   ILogger? logger = null)
    {
        _font   = font;
        _logger = logger;
        IsVisible = true;
        Position = new Vector2(4, 4);

        var mm = ui?.GetItem("UIWindow2.img/MiniMap") as WzProperty;
        LoadFrame(loader, mm?.Get("MinMap") as WzProperty, _minMap);
        LoadFrame(loader, mm?.Get("MaxMap") as WzProperty, _maxMap);

        var strip = mm?.Get("Min") as WzProperty;
        _stripW = LoadCanvas(loader, strip, "w");
        _stripC = LoadCanvas(loader, strip, "c");
        _stripE = LoadCanvas(loader, strip, "e");

        _markers = new MiniMapMarkers(loader, ui);

        _btMin = MakeBtn(loader, mm, "BtMin", () => _mode = (Mode)Math.Max(0, (int)_mode - 1));
        _btMax = MakeBtn(loader, mm, "BtMax", () => _mode = (Mode)Math.Min(2, (int)_mode + 1));
        _btMap = MakeBtn(loader, mm, "BtMap", () => _logger?.LogInformation("MiniMap: World Map not implemented yet"));
    }

    // ── Feed (called by GameStage) ──────────────────────────────────────────────

    /// <summary>Bind the active field's minimap data + names. Call on SetField.</summary>
    public void SetField(MiniMapData? data, string street, string mapName)
    {
        _data       = data;
        _streetName = street;
        _mapName    = mapName;
    }

    public void SetNpcs(IEnumerable<Vector2> npcs)       { _npcs.Clear();    _npcs.AddRange(npcs); }
    public void SetOtherPlayers(IEnumerable<Vector2> p)  { _others.Clear();  _others.AddRange(p); }
    public void SetPortals(IEnumerable<Vector2> portals) { _portals.Clear(); _portals.AddRange(portals); }

    /// <summary>Cycle Min → Normal → Max → Min (the 'M' hotkey path).</summary>
    public void CycleMode()
    {
        IsVisible = true;
        _mode = (Mode)(((int)_mode + 1) % 3);
    }

    // ── Update ───────────────────────────────────────────────────────────────

    public override void Update(GameTime gameTime)
    {
        var win = WindowRect();
        LayoutButtons(win);
        var mouse = Mouse.GetState();
        foreach (var b in _buttons) b.Update(mouse.X, mouse.Y, mouse.LeftButton == ButtonState.Pressed);
    }

    // ── Draw ───────────────────────────────────────────────────────────────────

    public override void Draw(SpriteBatch sb, Texture2D white)
    {
        if (!IsVisible) return;

        var win = WindowRect();
        if (_mode == Mode.Min) { DrawCollapsed(sb, white, win); return; }

        var frame = _mode == Mode.Max ? _maxMap : _minMap;
        var scale = _mode == Mode.Max ? 2 : 1;

        // Pass A — solid dark window backing behind everything. (The frame's "c" tile
        // carries rounded-corner art rather than a flat fill, so a plain dark rect is
        // both safer and visually closer to the translucent v95 minimap interior.)
        sb.Draw(white, win, new Color(17, 17, 28, 220));

        // Content (map) rectangle inside the frame, below the title bar.
        var pane = new Rectangle(
            win.X + frame.BorderL,
            win.Y + frame.TitleH,
            win.Width  - frame.BorderL - frame.BorderR,
            win.Height - frame.TitleH  - frame.BorderB);

        DrawMapAndIcons(sb, white, pane, scale);

        // Pass B — frame edges + corners on top (their inner areas are transparent,
        // so they crop/border the map without hiding it).
        DrawFrame(sb, frame, win);

        // Region emblem (info/mapMark) at the top-left — only in the fully-maximised view.
        if (_mode == Mode.Max)
            _data?.Mark?.Draw(sb, new Vector2(win.X + MarkX, win.Y + MarkY));

        // Title text + buttons on top of the frame.
        DrawTitle(sb, win, frame.NwW, frame.TitleH);
        foreach (var b in _buttons) b.Draw(sb);
    }

    // Top-left offset of the map-mark emblem within the minimap window (Max view only).
    private const int MarkX = 7;
    private const int MarkY = 17;

    private void DrawMapAndIcons(SpriteBatch sb, Texture2D white, Rectangle pane, int scale)
    {
        // The canvas blit is clipped exactly by its source/dest rectangles (it never
        // exceeds the pane), and icons are clipped against the pane manually — so no
        // scissor is needed and the shared SpriteBatch is left untouched.
        if (_data?.Canvas is { } canvas)
        {
            var player = _data.WorldToCanvas(PlayerWorldPos);
            var ax = ComputeAxis(player.X, _data.CanvasWidth,  pane.Width,  scale);
            var ay = ComputeAxis(player.Y, _data.CanvasHeight, pane.Height, scale);

            sb.Draw(canvas.Texture,
                new Rectangle(pane.X + ax.DrawOffset, pane.Y + ay.DrawOffset,
                              ax.ViewLen * scale, ay.ViewLen * scale),
                new Rectangle(ax.ViewStart, ay.ViewStart, ax.ViewLen, ay.ViewLen),
                Color.White);

            // Icons (player drawn last, on top).
            foreach (var p in _portals) DrawMarker(sb, pane, ax, ay, scale, p, _markers.Portal,  clamp: false);
            foreach (var n in _npcs)    DrawMarker(sb, pane, ax, ay, scale, n, _markers.Npc,     clamp: false);
            foreach (var o in _others)  DrawMarker(sb, pane, ax, ay, scale, o, _markers.Another, clamp: true);
            DrawMarker(sb, pane, ax, ay, scale, PlayerWorldPos, _markers.User, clamp: false);
        }
        else
        {
            // No per-map minimap: neutral fill so the frame still reads.
            sb.Draw(white, pane, new Color(28, 36, 28, 200));
        }
    }

    // v95 draws simple-minimap icons at 2× scale, anchored bottom-centre on the marker point
    // (CUIMiniMap::DrawIcon: x - w, y - 2h, sized 2w×2h). The DefaultHelper icons are designed for it.
    private const int MarkerScale = 2;

    // Plots one marker. canvasPt → pane pixel; if inside the pane draw the icon (2× bottom-centre),
    // otherwise (when clamp==true) draw a directional edge arrow at the border.
    private void DrawMarker(SpriteBatch sb, Rectangle pane, Axis ax, Axis ay, int scale,
                            Vector2 world, WzSprite? icon, bool clamp)
    {
        if (icon is null || _data is null) return;
        var c = _data.WorldToCanvas(world);
        var px = pane.X + ax.DrawOffset + (c.X - ax.ViewStart) * scale;
        var py = pane.Y + ay.DrawOffset + (c.Y - ay.ViewStart) * scale;

        var inside = px >= pane.X && px <= pane.Right && py >= pane.Y && py <= pane.Bottom;
        if (inside)
        {
            DrawIconBottomCentre(sb, icon, px, py);
            return;
        }
        if (!clamp) return;

        var ex = px < pane.X ? -1 : px > pane.Right  ? 1 : 0;
        var ey = py < pane.Y ? -1 : py > pane.Bottom ? 1 : 0;
        var arrow = _markers.EdgeArrow(ex, ey);
        if (arrow is null) return;
        var cx = Math.Clamp(px, pane.X, pane.Right);
        var cy = Math.Clamp(py, pane.Y, pane.Bottom);
        DrawIconBottomCentre(sb, arrow, cx, cy);
    }

    // Blit an icon at MarkerScale×, with its bottom edge centred on (px,py).
    private static void DrawIconBottomCentre(SpriteBatch sb, WzSprite icon, int px, int py)
    {
        var w = icon.Width * MarkerScale;
        var h = icon.Height * MarkerScale;
        sb.Draw(icon.Texture, new Rectangle(px - w / 2, py - h, w, h), Color.White);
    }

    private void DrawCollapsed(SpriteBatch sb, Texture2D white, Rectangle win)
    {
        if (_stripW != null && _stripC != null && _stripE != null)
        {
            _stripW.Draw(sb, new Vector2(win.X, win.Y));
            var midX = win.X + _stripW.Width;
            var midW = win.Width - _stripW.Width - _stripE.Width;
            if (midW > 0)
                sb.Draw(_stripC.Texture, new Rectangle(midX, win.Y, midW, _stripC.Height), Color.White);
            _stripE.Draw(sb, new Vector2(win.Right - _stripE.Width, win.Y));
        }
        else
        {
            sb.Draw(white, win, new Color(17, 17, 28, 230));
        }

        DrawTitle(sb, win, _stripW?.Width ?? 64, StripH);
        foreach (var b in _buttons) b.Draw(sb);
    }

    private void DrawTitle(SpriteBatch sb, Rectangle win, int tabW, int bandH)
    {
        if (_font is null) return;
        var ty = win.Y + (bandH - _font.LineHeight) / 2;
        var tx = win.X + tabW - 8;   // start just past the baked "MINI MAP" tab

        if (_mode == Mode.Max && !string.IsNullOrEmpty(_streetName))
        {
            _font.Draw(sb, _streetName, new Vector2(tx, win.Y + 4), new Color(170, 170, 130));
            _font.Draw(sb, _mapName,    new Vector2(tx, win.Y + 4 + _font.LineHeight), Color.White);
        }
        else if (!string.IsNullOrEmpty(_mapName))
        {
            _font.Draw(sb, _mapName, new Vector2(tx, ty), Color.White);
        }
    }

    private static void DrawFrame(SpriteBatch sb, FrameSet f, Rectangle win)
    {
        // Corners
        f.Nw?.Draw(sb, new Vector2(win.X, win.Y));
        f.Ne?.Draw(sb, new Vector2(win.Right - (f.Ne?.Width ?? 0), win.Y));
        f.Sw?.Draw(sb, new Vector2(win.X, win.Bottom - (f.Sw?.Height ?? 0)));
        f.Se?.Draw(sb, new Vector2(win.Right - (f.Se?.Width ?? 0), win.Bottom - (f.Se?.Height ?? 0)));

        var nwW = f.Nw?.Width ?? 0; var neW = f.Ne?.Width ?? 0;
        var swW = f.Sw?.Width ?? 0; var seW = f.Se?.Width ?? 0;

        // Top / bottom edges (stretch horizontally)
        if (f.N != null)
            StretchH(sb, f.N, win.X + nwW, win.Y, win.Width - nwW - neW);
        if (f.S != null)
            StretchH(sb, f.S, win.X + swW, win.Bottom - f.S.Height, win.Width - swW - seW);

        // Left / right edges (stretch vertically)
        var nwH = f.Nw?.Height ?? 0; var swH = f.Sw?.Height ?? 0;
        var neH = f.Ne?.Height ?? 0; var seH = f.Se?.Height ?? 0;
        if (f.W != null)
            StretchV(sb, f.W, win.X, win.Y + nwH, win.Height - nwH - swH);
        if (f.E != null)
            StretchV(sb, f.E, win.Right - f.E.Width, win.Y + neH, win.Height - neH - seH);
    }

    private static void StretchH(SpriteBatch sb, WzSprite s, int x, int y, int w)
    {
        if (w <= 0) return;
        sb.Draw(s.Texture, new Rectangle(x, y, w, s.Height), Color.White);
    }

    private static void StretchV(SpriteBatch sb, WzSprite s, int x, int y, int h)
    {
        if (h <= 0) return;
        sb.Draw(s.Texture, new Rectangle(x, y, s.Width, h), Color.White);
    }

    // ── Geometry ───────────────────────────────────────────────────────────────

    private Rectangle WindowRect()
    {
        var x = (int)Position.X;
        var y = (int)Position.Y;

        if (_mode == Mode.Min)
        {
            var nameW = _font != null && !string.IsNullOrEmpty(_mapName) ? (int)_font.Measure(_mapName).X : 0;
            var tabW  = _stripW?.Width ?? 64;
            var w = tabW + nameW + 6 + ButtonsWidth() + TitlePadRight;
            return new Rectangle(x, y, Math.Max(w, 160), StripH);
        }

        var frame = _mode == Mode.Max ? _maxMap : _minMap;
        var scale = _mode == Mode.Max ? 2 : 1;
        var capW  = _mode == Mode.Max ? MaxPaneCapW : NormalPaneCapW;
        var capH  = _mode == Mode.Max ? MaxPaneCapH : NormalPaneCapH;

        var cw = _data?.CanvasWidth  ?? 180;
        var ch = _data?.CanvasHeight ?? 120;
        var paneW = Math.Min(cw * scale, capW);
        var paneH = Math.Min(ch * scale, capH);

        // Keep the title bar wide enough for the tab + name + buttons.
        var titleNeed = frame.NwW + (_font != null && !string.IsNullOrEmpty(_mapName) ? (int)_font.Measure(_mapName).X : 0)
                        + 6 + ButtonsWidth() + TitlePadRight;
        paneW = Math.Max(paneW, titleNeed - frame.BorderL - frame.BorderR);

        return new Rectangle(x, y,
            paneW + frame.BorderL + frame.BorderR,
            paneH + frame.TitleH + frame.BorderB);
    }

    private int ButtonsWidth()
    {
        var w = 0;
        foreach (var b in new[] { _btMin, _btMax, _btMap })
            if (b != null) w += b.Bounds.Width + BtnGap;
        return w;
    }

    private void LayoutButtons(Rectangle win)
    {
        var borderR = _mode == Mode.Min ? (_stripE?.Width ?? 9) : (_mode == Mode.Max ? _maxMap.BorderR : _minMap.BorderR);
        var bandH   = _mode == Mode.Min ? StripH : (_mode == Mode.Max ? _maxMap.TitleH : _minMap.TitleH);

        var bx = win.Right - borderR - ButtonsWidth() + BtnGap;
        var by = win.Y + Math.Max(2, (Math.Min(bandH, 21) - 12) / 2);
        foreach (var b in new[] { _btMin, _btMax, _btMap })
        {
            if (b is null) continue;
            b.Position = new Vector2(bx, by);
            bx += b.Bounds.Width + BtnGap;
        }
    }

    private readonly record struct Axis(int ViewStart, int ViewLen, int DrawOffset);

    // Per-axis scroll: centre the canvas if it fits the pane, else follow the player clamped.
    private static Axis ComputeAxis(int playerCanvas, int canvasLen, int paneLen, int scale)
    {
        var scaledLen = canvasLen * scale;
        if (scaledLen <= paneLen)
            return new Axis(0, canvasLen, (paneLen - scaledLen) / 2);
        var viewLen   = Math.Max(1, paneLen / scale);
        var viewStart = Math.Clamp(playerCanvas - viewLen / 2, 0, Math.Max(0, canvasLen - viewLen));
        return new Axis(viewStart, viewLen, 0);
    }

    // ── Input ────────────────────────────────────────────────────────────────

    public override bool HandleMouseButton(int x, int y, bool down)
    {
        if (!IsVisible) return false;
        foreach (var b in _buttons)
            if (b.HandleMouseButton(x, y, down)) return true;
        return WindowRect().Contains(x, y);
    }

    // ── Asset loading helpers ───────────────────────────────────────────────────

    private static void LoadFrame(WzTextureLoader loader, WzProperty? root, FrameSet f)
    {
        f.Nw = LoadCanvas(loader, root, "nw"); f.N = LoadCanvas(loader, root, "n"); f.Ne = LoadCanvas(loader, root, "ne");
        f.W  = LoadCanvas(loader, root, "w");  f.C = LoadCanvas(loader, root, "c"); f.E  = LoadCanvas(loader, root, "e");
        f.Sw = LoadCanvas(loader, root, "sw"); f.S = LoadCanvas(loader, root, "s"); f.Se = LoadCanvas(loader, root, "se");
    }

    private static WzSprite? LoadCanvas(WzTextureLoader loader, WzProperty? root, string name)
        => root?.Get(name) is WzCanvas c ? loader.Load(c) : null;

    private Button? MakeBtn(WzTextureLoader loader, WzProperty? root, string name, Action onClick)
    {
        if (root?.Get(name) is not WzProperty pr) return null;
        var b = new Button(loader, pr) { OnClick = onClick };
        _buttons.Add(b);
        return b;
    }
}
