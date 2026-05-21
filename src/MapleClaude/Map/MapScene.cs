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
///   <item>Only layer 0 obj sprites — no layers 1-7, no tiles, no foothold/portal/etc.</item>
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

    private readonly List<(BackInfo Info, WzSprite? Sprite)> _backgrounds = new();
    private readonly List<(BackInfo Info, WzSprite? Sprite)> _foregrounds = new();
    private readonly List<(ObjInfo Info, WzSprite? Sprite)> _layer0Objects = new();

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
                if (bg.Front)
                {
                    _foregrounds.Add((bg, sprite));
                }
                else
                {
                    _backgrounds.Add((bg, sprite));
                }
            }
            _logger.LogInformation(
                "Map scene loaded {BgCount} backdrops, {FgCount} foregrounds (bgm={Bgm})",
                _backgrounds.Count, _foregrounds.Count, BgmPath);
        }

        // Layer 0 objects (the production logo + animated mascots live here on login)
        if (mapRoot.Get("0") is WzProperty layer0
            && layer0.Get("obj") is WzProperty objRoot)
        {
            foreach (var (key, value) in objRoot.Items)
            {
                if (value is not WzProperty entry)
                {
                    continue;
                }
                var obj = ObjInfo.From(entry);
                var sprite = LoadObjSprite(obj);
                _layer0Objects.Add((obj, sprite));
            }
            _layer0Objects.Sort((a, b) => a.Info.Z.CompareTo(b.Info.Z));
            _logger.LogInformation("Map scene layer 0 loaded {ObjCount} objects", _layer0Objects.Count);
        }
    }

    public void Draw(SpriteBatch sb, Texture2D fillTexture, int screenWidth, int screenHeight)
    {
        var screenCenter = new Vector2(screenWidth / 2f, screenHeight / 2f);

        // Backdrops (behind world)
        foreach (var (info, sprite) in _backgrounds)
        {
            DrawBackEntry(sb, info, sprite, screenCenter, screenWidth, screenHeight);
        }

        // Layer 0 obj — the MapleStory logo, characters, etc.
        foreach (var (info, sprite) in _layer0Objects)
        {
            if (sprite == null)
            {
                continue;
            }
            var pos = ObjMapToScreen(info, screenCenter);
            sprite.Draw(sb, pos);
        }

        // Foregrounds (in front of world)
        foreach (var (info, sprite) in _foregrounds)
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
