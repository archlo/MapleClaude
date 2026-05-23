using System.Globalization;
using System.Linq;
using MapleClaude.Character;
using MapleClaude.Render;
using MapleClaude.Wz;
using Microsoft.Extensions.Logging;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace MapleClaude.Map;

/// <summary>
/// Extended in-game map renderer. Loads a full <c>...img</c> map blob
/// (info, back, layers 0..7, foothold, portal, life) and renders the
/// backdrops + tiles + objs every frame. Supports foothold-based
/// player placement and camera follow.
/// </summary>
public sealed class FieldScene
{
    private const int LayerCount = 8;

    private readonly ILogger<FieldScene> _logger;
    private readonly WzPackage _mapWz;
    private readonly WzTextureLoader _loader;
    private readonly Dictionary<int, Foothold> _footholds = new();
    private readonly Dictionary<int, Portal> _portals = new();
    private MapInfo _info = new();

    // Render data (loaded once per map, drawn every frame against the camera).
    private readonly List<BackDraw> _backs = new();
    private readonly List<BackDraw> _fronts = new();
    private readonly List<TileDraw>[] _tileLayers = NewLayerArray<TileDraw>();
    private readonly List<ObjDraw>[] _objLayers = NewLayerArray<ObjDraw>();
    private bool _loaded;

    private static List<T>[] NewLayerArray<T>()
    {
        var a = new List<T>[LayerCount];
        for (var i = 0; i < LayerCount; i++) a[i] = new List<T>();
        return a;
    }

    private sealed class BackDraw
    {
        public required BackInfo Info;
        public AnimatedSprite? Sprite;
        public double ScrollX;
        public double ScrollY;
    }

    private sealed class ObjDraw
    {
        public required ObjInfo Info;
        public AnimatedSprite? Sprite;
    }

    private sealed class TileDraw
    {
        public int X;
        public int Y;
        public int Z;
        public WzSprite? Sprite;
    }

    public Camera2D Camera { get; } = new();
    public MapInfo Info => _info;
    public IReadOnlyDictionary<int, Foothold> Footholds => _footholds;
    public IReadOnlyDictionary<int, Portal> Portals => _portals;

    public FieldScene(ILogger<FieldScene> logger, WzPackage mapWz, WzTextureLoader loader)
    {
        _logger = logger;
        _mapWz = mapWz;
        _loader = loader;
    }

    public void Load(int mapId)
    {
        var prefix = mapId / 100_000_000;
        var padded = mapId.ToString("D9", CultureInfo.InvariantCulture);
        var path = $"Map/Map{prefix}/{padded}.img";
        if (_mapWz.GetItem(path) is not WzImage img)
        {
            _logger.LogError("Map {Path} not found in Map.wz", path);
            return;
        }
        var root = img.Root;
        LoadInfo(root);
        LoadFootholds(root);
        LoadPortals(root);
        try
        {
            LoadBackgrounds(root);
            LoadLayers(root);
            _loaded = true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "FieldScene render-data load failed for map {Id}", mapId);
        }
        var tiles = _tileLayers.Sum(l => l.Count);
        var objs = _objLayers.Sum(l => l.Count);
        _logger.LogInformation(
            "FieldScene: map={Id} footholds={Fh} portals={P} backs={B} tiles={T} objs={O}",
            mapId, _footholds.Count, _portals.Count, _backs.Count + _fronts.Count, tiles, objs);
    }

    private void LoadInfo(WzProperty root)
    {
        if (root.Get("info") is not WzProperty info)
        {
            return;
        }
        var bgm = info.Get("bgm")?.ToString() ?? string.Empty;
        var returnMap = ReadInt(info, "returnMap");
        var forcedReturn = ReadInt(info, "forcedReturn");
        var fieldLimit = ReadInt(info, "fieldLimit");
        var mapDesc = info.Get("mapDesc")?.ToString() ?? string.Empty;
        var town = ReadInt(info, "town");
        _info = new MapInfo
        {
            Bgm = bgm,
            ReturnMap = returnMap,
            ForcedReturn = forcedReturn,
            FieldLimit = fieldLimit,
            MapDesc = mapDesc,
            Town = town,
            VRLeft = ReadInt(info, "VRLeft"),
            VRTop = ReadInt(info, "VRTop"),
            VRRight = ReadInt(info, "VRRight"),
            VRBottom = ReadInt(info, "VRBottom"),
        };
    }

    private void LoadFootholds(WzProperty root)
    {
        if (root.Get("foothold") is not WzProperty fhRoot)
        {
            return;
        }
        foreach (var (layerKey, layerNode) in fhRoot.Items)
        {
            if (layerNode is not WzProperty layer)
            {
                continue;
            }
            var layerIdx = int.TryParse(layerKey, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ln) ? ln : 0;
            foreach (var (groupKey, groupNode) in layer.Items)
            {
                if (groupNode is not WzProperty group)
                {
                    continue;
                }
                var groupIdx = int.TryParse(groupKey, NumberStyles.Integer, CultureInfo.InvariantCulture, out var gn) ? gn : 0;
                foreach (var (idStr, entryNode) in group.Items)
                {
                    if (entryNode is not WzProperty entry)
                    {
                        continue;
                    }
                    var id = int.TryParse(idStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;
                    _footholds[id] = new Foothold
                    {
                        Id = id,
                        Layer = layerIdx,
                        Group = groupIdx,
                        X1 = ReadInt(entry, "x1"),
                        Y1 = ReadInt(entry, "y1"),
                        X2 = ReadInt(entry, "x2"),
                        Y2 = ReadInt(entry, "y2"),
                        Prev = ReadInt(entry, "prev"),
                        Next = ReadInt(entry, "next"),
                        Force = ReadInt(entry, "force"),
                        CantThrough = ReadInt(entry, "cantThrough") != 0,
                        ForbidFallDown = ReadInt(entry, "forbidFallDown") != 0,
                    };
                }
            }
        }
    }

    private void LoadPortals(WzProperty root)
    {
        if (root.Get("portal") is not WzProperty portalRoot)
        {
            return;
        }
        foreach (var (idxStr, node) in portalRoot.Items)
        {
            if (node is not WzProperty entry)
            {
                continue;
            }
            var idx = int.TryParse(idxStr, out var v) ? v : 0;
            _portals[idx] = new Portal
            {
                Index = idx,
                Name = entry.Get("pn")?.ToString() ?? string.Empty,
                Type = ReadInt(entry, "pt"),
                X = ReadInt(entry, "x"),
                Y = ReadInt(entry, "y"),
                TargetMap = ReadInt(entry, "tm"),
                TargetPortal = entry.Get("tn")?.ToString() ?? string.Empty,
                Delay = ReadInt(entry, "delay"),
                OnlyOnce = ReadInt(entry, "onlyOnce") != 0,
            };
        }
    }

    // ── Render-data loading ─────────────────────────────────────────────────────

    private void LoadBackgrounds(WzProperty root)
    {
        if (root.Get("back") is not WzProperty backRoot) return;
        foreach (var (_, value) in backRoot.Items)
        {
            if (value is not WzProperty entry) continue;
            var info = BackInfo.From(entry);
            if (string.IsNullOrEmpty(info.Bs)) continue;
            var subTree = info.Animated ? "ani" : "back";
            var node = _mapWz.GetItem($"Back/{info.Bs}.img/{subTree}/{info.No}");
            var draw = new BackDraw { Info = info, Sprite = _loader.LoadAnimation(node) };
            (info.Front ? _fronts : _backs).Add(draw);
        }
    }

    private void LoadLayers(WzProperty root)
    {
        for (var layer = 0; layer < LayerCount; layer++)
        {
            if (root.Get(layer.ToString(CultureInfo.InvariantCulture)) is not WzProperty lp) continue;

            // Tiles: Map.wz/Tile/<tS>.img/<u>/<no>, tileset name from the layer info.
            var tileSet = (lp.Get("info") as WzProperty)?.Get("tS") as string;
            if (!string.IsNullOrEmpty(tileSet) && lp.Get("tile") is WzProperty tileRoot)
            {
                foreach (var (_, value) in tileRoot.Items)
                {
                    if (value is not WzProperty te) continue;
                    var u = te.Get("u") as string ?? string.Empty;
                    var no = ReadInt(te, "no");
                    var canvas = _mapWz.GetItem($"Tile/{tileSet}.img/{u}/{no}") as WzCanvas;
                    _tileLayers[layer].Add(new TileDraw
                    {
                        X = ReadInt(te, "x"),
                        Y = ReadInt(te, "y"),
                        Z = canvas is not null ? ReadInt(canvas.Property, "z") : 0,
                        Sprite = _loader.Load(canvas),
                    });
                }
                _tileLayers[layer].Sort((a, b) => a.Z.CompareTo(b.Z));
            }

            // Objects: Map.wz/Obj/<oS>.img/<l0>/<l1>/<l2> (animated).
            if (lp.Get("obj") is WzProperty objRoot)
            {
                foreach (var (_, value) in objRoot.Items)
                {
                    if (value is not WzProperty oe) continue;
                    var info = ObjInfo.From(oe);
                    var node = _mapWz.GetItem($"Obj/{info.Os}.img/{info.L0}/{info.L1}/{info.L2}");
                    _objLayers[layer].Add(new ObjDraw { Info = info, Sprite = _loader.LoadAnimation(node) });
                }
                _objLayers[layer].Sort((a, b) => a.Info.Z.CompareTo(b.Info.Z));
            }
        }
    }

    // ── Per-frame update + draw ──────────────────────────────────────────────────

    /// <summary>Advances background/object animations and background autoscroll.</summary>
    public void Update(double dtMs)
    {
        foreach (var b in _backs) UpdateBack(b, dtMs);
        foreach (var b in _fronts) UpdateBack(b, dtMs);
        foreach (var layer in _objLayers)
        {
            foreach (var o in layer) o.Sprite?.Update(dtMs);
        }
    }

    private static void UpdateBack(BackDraw b, double dtMs)
    {
        b.Sprite?.Update(dtMs);
        var dtSec = dtMs / 1000.0;
        if (b.Info.Type is BackType.HMoveA or BackType.HMoveB) b.ScrollX += b.Info.Rx * dtSec;
        if (b.Info.Type is BackType.VMoveA or BackType.VMoveB) b.ScrollY += b.Info.Ry * dtSec;
    }

    public void Draw(SpriteBatch sb, Texture2D whitePixel, int screenWidth, int screenHeight)
    {
        if (!_loaded)
        {
            sb.Draw(whitePixel, new Rectangle(0, 0, screenWidth, screenHeight), new Color(8, 8, 20));
            return;
        }
        var center = new Vector2(screenWidth / 2f, screenHeight / 2f);

        foreach (var b in _backs) DrawBackground(sb, b, center, screenWidth, screenHeight);

        for (var layer = 0; layer < LayerCount; layer++)
        {
            foreach (var t in _tileLayers[layer])
            {
                t.Sprite?.Draw(sb, WorldToScreen(t.X, t.Y, center));
            }
            foreach (var o in _objLayers[layer])
            {
                if (o.Sprite is null) continue;
                var fx = o.Info.Flip ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
                o.Sprite.Draw(sb, WorldToScreen(o.Info.X, o.Info.Y, center), fx);
            }
        }

        foreach (var b in _fronts) DrawBackground(sb, b, center, screenWidth, screenHeight);
    }

    // Matches GameCamera.WorldToScreen so the map aligns with mobs/drops/player.
    private Vector2 WorldToScreen(float worldX, float worldY, Vector2 center) =>
        new(worldX - Camera.Position.X + center.X, worldY - Camera.Position.Y + center.Y);

    private void DrawBackground(SpriteBatch sb, BackDraw b, Vector2 center, int w, int h)
    {
        var spr = b.Sprite;
        if (spr is null) return;
        var info = b.Info;
        var color = info.Alpha < 255 ? new Color((byte)255, (byte)255, (byte)255, info.Alpha) : (Color?)null;
        var flip = info.Flip ? SpriteEffects.FlipHorizontally : SpriteEffects.None;

        // Parallax: rx/ry = 0 moves 1:1 with the map; -100 is fixed to the screen.
        var bx = info.X + center.X - (float)(Camera.Position.X * (info.Rx + 100) / 100.0);
        var by = info.Y + center.Y - (float)(Camera.Position.Y * (info.Ry + 100) / 100.0);

        var hMove = info.Type is BackType.HMoveA or BackType.HMoveB;
        var vMove = info.Type is BackType.VMoveA or BackType.VMoveB;
        if (hMove) bx = info.X + center.X + (float)b.ScrollX;   // autoscroll, screen-anchored X
        if (vMove) by = info.Y + center.Y + (float)b.ScrollY;

        var hTile = info.Type is BackType.HTiled or BackType.Tiled || hMove;
        var vTile = info.Type is BackType.VTiled or BackType.Tiled || vMove;

        var px = info.Cx > 0 ? info.Cx : Math.Max(1, spr.Width);
        var py = info.Cy > 0 ? info.Cy : Math.Max(1, spr.Height);

        // Bring the tiled start just left/above the screen.
        var x0 = bx;
        var y0 = by;
        if (hTile) { x0 %= px; if (x0 > 0) x0 -= px; }
        if (vTile) { y0 %= py; if (y0 > 0) y0 -= py; }

        var xStart = hTile ? x0 : bx;
        var yStart = vTile ? y0 : by;
        var xEnd = hTile ? w + px : bx + 1f;
        var yEnd = vTile ? h + py : by + 1f;
        var xStep = hTile ? px : int.MaxValue;
        var yStep = vTile ? py : int.MaxValue;

        for (var yy = yStart; yy < yEnd; yy += yStep)
        {
            for (var xx = xStart; xx < xEnd; xx += xStep)
            {
                spr.Draw(sb, new Vector2(xx, yy), flip, color);
            }
        }
    }

    public void PlacePlayerAtPortal(PlayerController player, byte portalIndex)
    {
        if (!_portals.TryGetValue(portalIndex, out var portal))
        {
            // Default portal index 0 if specific one missing.
            if (!_portals.TryGetValue(0, out portal))
            {
                _logger.LogWarning("No portals — spawning player at (0,0)");
                player.Position = Vector2.Zero;
                return;
            }
        }
        player.Position = portal.Position;
        Camera.Position = portal.Position;
        _logger.LogInformation("Player placed at portal {Idx} ({X},{Y})", portalIndex, portal.X, portal.Y);
    }

    private static int ReadInt(WzProperty p, string key)
    {
        var v = p.Get(key);
        return v switch
        {
            int i => i,
            short s => s,
            byte b => b,
            long l => (int)l,
            string s => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0,
            null => 0,
            _ => 0,
        };
    }
}
