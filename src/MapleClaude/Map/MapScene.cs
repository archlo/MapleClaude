using MapleClaude.Render;
using MapleClaude.Wz;
using Microsoft.Extensions.Logging;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace MapleClaude.Map;

/// <summary>
/// A single map's render state. Loads parallax backdrops from <c>back/0..N</c>
/// and (optionally) the simple obj sprites from layer 0. The login screen
/// uses this to render <c>UI.wz/MapLogin1.img</c> as the title backdrop.
///
/// Phase 1 scope:
/// <list type="bullet">
///   <item>Only static backdrop sprites — no scrolling, no animation frames beyond frame 0.</item>
///   <item>Obj sprites from layers 0-7 (tiles, foothold, portal, etc. still skipped).</item>
///   <item>BackType.Normal positions sprite at (Bg.X, Bg.Y) + camera offset.</item>
///   <item>BackType.HTiled / VTiled / Tiled repeat the sprite across the screen.</item>
///   <item>BackType.HMove* / VMove* render once (no scroll yet).</item>
/// </list>
/// </summary>
public sealed class MapScene
{
    private readonly ILogger _logger;
    private readonly WzPackage _mapPkg;
    private readonly WzTextureLoader _loader;

    private readonly List<(int Index, BackInfo Info, WzSprite? Sprite)> _backgrounds = new();
    private readonly List<(int Index, BackInfo Info, WzSprite? Sprite)> _foregrounds = new();
    private readonly List<(int Layer, ObjInfo Info, WzSprite? Sprite)> _objects = new();

    /// <summary>BGM path from the map's <c>info/bgm</c> property (e.g. <c>"BgmUI/Title"</c>).</summary>
    public string? BgmPath { get; private set; }

    /// <summary>
    /// World-coordinate position of the map's <c>sp</c> (start point) portal.
    /// In v95 maps this is the player spawn — for the login map it's the
    /// true "title section" centre. <c>null</c> if no SP portal is defined.
    /// </summary>
    public Vector2? StartPoint { get; private set; }

    /// <summary>
    /// Camera position in map coordinates. Backdrops/obj sprites render at
    /// <c>(info.X - Camera.X + screenCenter, info.Y - Camera.Y + screenCenter)</c>.
    /// For the login scene we tune this experimentally.
    /// </summary>
    public Vector2 Camera { get; set; }

    public MapScene(ILogger logger, WzPackage mapPkg, WzTextureLoader loader)
    {
        _logger = logger;
        _mapPkg = mapPkg;
        _loader = loader;
    }

    /// <summary>Loads a map blob from a property tree (e.g. <c>UI.wz/MapLogin1.img</c>).</summary>
    public void Load(WzProperty mapRoot)
    {
        // Info
        var info = mapRoot.Get("info") as WzProperty;
        BgmPath = info?.GetOrDefault<string>("bgm");

        // Find the SP (start point) portal — this is the "true centre" of
        // the title section for the login map. v95 maps store portals at
        // mapRoot/portal/<N>, each with properties pn, pt, x, y, tm, tn.
        if (mapRoot.Get("portal") is WzProperty portalRoot)
        {
            foreach (var (_, value) in portalRoot.Items)
            {
                if (value is WzProperty portal
                    && string.Equals(portal.GetOrDefault<string>("pn"), "sp", StringComparison.Ordinal))
                {
                    var sx = ReadInt(portal, "x");
                    var sy = ReadInt(portal, "y");
                    StartPoint = new Vector2(sx, sy);
                    _logger.LogInformation("Map SP portal at ({X}, {Y})", sx, sy);
                    break;
                }
            }
        }

        // Backdrops
        if (mapRoot.Get("back") is WzProperty backRoot)
        {
            foreach (var (key, value) in backRoot.Items)
            {
                if (value is not WzProperty entry)
                {
                    continue;
                }
                var bg = BackInfo.From(entry);
                var sprite = LoadBackSprite(bg);
                var idx = int.TryParse(key, out var k) ? k : 0;
                if (bg.Front)
                {
                    _foregrounds.Add((idx, bg, sprite));
                }
                else
                {
                    _backgrounds.Add((idx, bg, sprite));
                }
            }
            // Draw in numeric back-index order (back/0 furthest behind ... back/N
            // in front). WzProperty.Items preserves WZ read order, which is NOT
            // numeric here, so the per-step sky (e.g. back/1) would otherwise paint
            // over its scene backdrop (e.g. back/29) — leaving char-select blank sky.
            _backgrounds.Sort((a, b) => a.Index.CompareTo(b.Index));
            _foregrounds.Sort((a, b) => a.Index.CompareTo(b.Index));
            _logger.LogInformation(
                "Map scene loaded {BgCount} backdrops, {FgCount} foregrounds (bgm={Bgm})",
                _backgrounds.Count, _foregrounds.Count, BgmPath);
        }

        // Object layers 0-7. The login map keeps the production logo + mascots on
        // layer 0, but the world/char-select signboards and their decorations live
        // on higher layers — load them all so the scrolling map renders natively.
        for (var layerIdx = 0; layerIdx < 8; layerIdx++)
        {
            if (mapRoot.Get(layerIdx.ToString(System.Globalization.CultureInfo.InvariantCulture)) is not WzProperty layer
                || layer.Get("obj") is not WzProperty objRoot)
            {
                continue;
            }
            foreach (var (_, value) in objRoot.Items)
            {
                if (value is not WzProperty entry)
                {
                    continue;
                }
                var obj = ObjInfo.From(entry);
                // The login frame (Common/frame, one per step) is drawn as a centred
                // UI overlay by the stages, so skip the map-object copy to avoid a
                // doubled/offset border. Everything else stays — including
                // WorldSelect/dual, the 800x576 ship-deck that IS the world-select
                // background, and the per-step signboards.
                if (obj.L0 == "Common" && obj.L1 == "frame")
                {
                    continue;
                }
                var sprite = LoadObjSprite(obj);
                if (sprite is null)
                {
                    continue;
                }
                _objects.Add((layerIdx, obj, sprite));
            }
        }
        // Draw order: lower layer first, then per-object z within a layer.
        _objects.Sort((a, b) => a.Layer != b.Layer ? a.Layer.CompareTo(b.Layer) : a.Info.Z.CompareTo(b.Info.Z));
        _logger.LogInformation("Map scene loaded {ObjCount} objects across layers 0-7", _objects.Count);
    }

    public void Draw(SpriteBatch sb, Texture2D fillTexture, int screenWidth, int screenHeight)
    {
        var screenCenter = new Vector2(screenWidth / 2f, screenHeight / 2f);

        // Backdrops (behind world)
        foreach (var (_, info, sprite) in _backgrounds)
        {
            DrawBackEntry(sb, info, sprite, screenCenter, screenWidth, screenHeight);
        }

        // Object layers (logo, mascots, world/char-select signboards + decorations).
        foreach (var (_, info, sprite) in _objects)
        {
            if (sprite == null)
            {
                continue;
            }
            sprite.Draw(sb, ObjMapToScreen(info, screenCenter));
        }

        // Foregrounds (in front of world)
        foreach (var (_, info, sprite) in _foregrounds)
        {
            DrawBackEntry(sb, info, sprite, screenCenter, screenWidth, screenHeight);
        }
    }

    private Vector2 BackMapToScreen(BackInfo bg, Vector2 screenCenter)
    {
        var x = bg.X + screenCenter.X - Camera.X;
        var y = bg.Y + screenCenter.Y - Camera.Y;
        return new Vector2(x, y);
    }

    private Vector2 ObjMapToScreen(ObjInfo obj, Vector2 screenCenter)
    {
        var x = obj.X + screenCenter.X - Camera.X;
        var y = obj.Y + screenCenter.Y - Camera.Y;
        return new Vector2(x, y);
    }

    /// <summary>
    /// Maps a world/map coordinate to screen space using the current camera — the
    /// same transform the scene uses for objects. Stages position map-space widgets
    /// (e.g. the world-select buttons that sit on the signboard) with this so they
    /// stay aligned with map objects regardless of scroll.
    /// </summary>
    public Vector2 WorldToScreen(Vector2 mapPos, int screenWidth, int screenHeight) =>
        new(mapPos.X + screenWidth / 2f - Camera.X, mapPos.Y + screenHeight / 2f - Camera.Y);

    private void DrawBackEntry(SpriteBatch sb, BackInfo info, WzSprite? sprite, Vector2 screenCenter, int screenWidth, int screenHeight)
    {
        if (sprite == null)
        {
            return;
        }
        var basePos = BackMapToScreen(info, screenCenter);

        switch (info.Type)
        {
            case BackType.Normal:
            case BackType.HMoveA:
            case BackType.HMoveB:
            case BackType.VMoveA:
            case BackType.VMoveB:
                sprite.Draw(sb, basePos);
                break;

            case BackType.HTiled:
                TileHorizontal(sb, sprite, basePos, info, screenWidth);
                break;

            case BackType.VTiled:
                TileVertical(sb, sprite, basePos, info, screenHeight);
                break;

            case BackType.Tiled:
                TileBoth(sb, sprite, basePos, info, screenWidth, screenHeight);
                break;
        }
    }

    private static void TileHorizontal(SpriteBatch sb, WzSprite sprite, Vector2 basePos, BackInfo info, int screenWidth)
    {
        var period = info.Cx > 0 ? info.Cx : sprite.Width;
        if (period <= 0)
        {
            sprite.Draw(sb, basePos);
            return;
        }
        // Walk left from basePos until off-screen, then right.
        var x = basePos.X;
        while (x - sprite.Origin.X > -period)
        {
            x -= period;
        }
        var maxX = screenWidth + period;
        for (var px = x; px < maxX; px += period)
        {
            sprite.Draw(sb, new Vector2(px, basePos.Y));
        }
    }

    private static void TileVertical(SpriteBatch sb, WzSprite sprite, Vector2 basePos, BackInfo info, int screenHeight)
    {
        var period = info.Cy > 0 ? info.Cy : sprite.Height;
        if (period <= 0)
        {
            sprite.Draw(sb, basePos);
            return;
        }
        var y = basePos.Y;
        while (y - sprite.Origin.Y > -period)
        {
            y -= period;
        }
        var maxY = screenHeight + period;
        for (var py = y; py < maxY; py += period)
        {
            sprite.Draw(sb, new Vector2(basePos.X, py));
        }
    }

    private static void TileBoth(SpriteBatch sb, WzSprite sprite, Vector2 basePos, BackInfo info, int screenWidth, int screenHeight)
    {
        var px = info.Cx > 0 ? info.Cx : sprite.Width;
        var py = info.Cy > 0 ? info.Cy : sprite.Height;
        if (px <= 0 || py <= 0)
        {
            sprite.Draw(sb, basePos);
            return;
        }
        var sx = basePos.X;
        while (sx - sprite.Origin.X > -px)
        {
            sx -= px;
        }
        var sy = basePos.Y;
        while (sy - sprite.Origin.Y > -py)
        {
            sy -= py;
        }
        for (var y = sy; y < screenHeight + py; y += py)
        {
            for (var x = sx; x < screenWidth + px; x += px)
            {
                sprite.Draw(sb, new Vector2(x, y));
            }
        }
    }

    private static int ReadInt(WzProperty p, string key)
    {
        return p.Get(key) switch
        {
            int i => i,
            short s => s,
            long l => (int)l,
            _ => 0,
        };
    }

    private WzSprite? LoadBackSprite(BackInfo bg)
    {
        // Resolve Map.wz/Back/<bS>.img/{back|ani}/<no>. The ani path is for animated
        // backdrops; phase-1 uses frame 0 even when ani=1.
        var subTree = bg.Animated ? "ani" : "back";
        var path = $"Back/{bg.Bs}.img/{subTree}/{bg.No}";
        var node = _mapPkg.GetItem(path);

        // For animated backdrops, the resolved node is a property containing frames;
        // frame 0 is the canvas.
        if (node is WzProperty animProp && animProp.Get("0") is WzCanvas firstFrame)
        {
            return _loader.Load(firstFrame);
        }
        return _loader.Load(node as WzCanvas);
    }

    private WzSprite? LoadObjSprite(ObjInfo obj)
    {
        // Resolve Map.wz/Obj/<oS>.img/<l0>/<l1>/<l2>. The leaf is usually a property
        // with numbered frame children (0, 1, 2 = animation frames) — use frame 0.
        var path = $"Obj/{obj.Os}.img/{obj.L0}/{obj.L1}/{obj.L2}";
        var node = _mapPkg.GetItem(path);
        if (node is WzProperty animProp && animProp.Get("0") is WzCanvas firstFrame)
        {
            return _loader.Load(firstFrame);
        }
        return _loader.Load(node as WzCanvas);
    }
}
