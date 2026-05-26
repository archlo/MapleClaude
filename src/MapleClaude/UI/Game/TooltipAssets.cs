using MapleClaude.Character;
using MapleClaude.Render;
using MapleClaude.Wz;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace MapleClaude.UI.Game;

/// <summary>
/// Loads the pre-baked bitmap canvases that the v95 client uses to render an equip tooltip body —
/// <c>UIWindow2.img/ToolTip/Equip/{Can,Cannot,Dot,WeaponCategory,ItemCategory,Speed,Property,
/// GrowthEnabled,GrowthDisabled,Star}</c> plus the <c>mesos</c>/<c>cash</c> tags. The original
/// <c>CUIToolTip</c> composes the requirement rows, the class strip, and the dotline separators
/// out of these canvases (no GDI text for any of them); the background box is the only
/// code-drawn piece (<c>CUIToolTip::InitCanvas</c>). Mirrors the <see cref="ItemIconLoader"/>
/// lazy-cache pattern: misses are cached too so repeated draws don't re-walk the WZ tree.
/// </summary>
public sealed class TooltipAssets
{
    private readonly WzTextureLoader _loader;
    private readonly WzProperty? _root;          // UIWindow2.img/ToolTip/Equip
    private readonly Dictionary<string, WzSprite?> _cache = new(System.StringComparer.Ordinal);

    public TooltipAssets(WzTextureLoader loader, WzPackage? uiWz)
    {
        _loader = loader;
        _root = uiWz?.GetItem("UIWindow2.img/ToolTip/Equip") as WzProperty;
    }

    /// <summary>True when the WZ subtree was resolved on construction. False means the .wz lacks
    /// <c>UIWindow2.img/ToolTip/Equip</c> (very old WZ) — the tooltip will fall back to font-only.</summary>
    public bool IsAvailable => _root is not null;

    /// <summary>Resolve any subpath (e.g. <c>"Can/reqLEV"</c>, <c>"WeaponCategory/30"</c>) into a
    /// loaded sprite. Returns the cached entry (or cached null) on subsequent calls.</summary>
    public WzSprite? Get(string subPath)
    {
        if (_cache.TryGetValue(subPath, out var cached))
        {
            return cached;
        }

        WzSprite? sprite = null;
        if (_root is not null)
        {
            // WzProperty.GetItem already splits on '/' and resolves nested nodes.
            var node = _root.GetItem(subPath);
            if (node is WzUol uol)
            {
                node = uol.Resolve();
            }
            if (node is WzCanvas canvas)
            {
                sprite = _loader.Load(canvas);
            }
        }
        _cache[subPath] = sprite;
        return sprite;
    }

    /// <summary>Requirement-row label (<c>reqLEV</c>/<c>reqSTR</c>/<c>reqDEX</c>/<c>reqINT</c>/
    /// <c>reqLUK</c>/<c>reqPOP</c>). The <paramref name="met"/> flag picks the green/white
    /// <c>Can/</c> variant; <c>false</c> picks the red <c>Cannot/</c> variant.</summary>
    public WzSprite? Req(string key, bool met) => Get($"{(met ? "Can" : "Cannot")}/{key}");

    /// <summary>Job-class label (<c>beginner</c>/<c>warrior</c>/<c>magician</c>/<c>bowman</c>/
    /// <c>thief</c>/<c>pirate</c>). The <paramref name="greyed"/> flag picks the <c>Cannot/</c>
    /// greyed variant; <c>false</c> picks the <c>Can/</c> bright variant.</summary>
    public WzSprite? JobLabel(string klass, bool greyed) =>
        Get($"{(greyed ? "Cannot" : "Can")}/{klass}");

    /// <summary>Single bitmap digit (or the "none" dash when <paramref name="d"/> is &lt; 0).</summary>
    public WzSprite? Digit(int d, bool met)
    {
        var ns = met ? "Can" : "Cannot";
        if (d is < 0 or > 9) return Get($"{ns}/none");
        return Get($"{ns}/{d}");
    }

    public WzSprite? Dot(int index) => Get($"Dot/{index}");
    public WzSprite? Property(int index) => Get($"Property/{index}");
    public WzSprite? Speed(int index) => Get($"Speed/{index}");
    public WzSprite? WeaponCategory(int index) => Get($"WeaponCategory/{index}");
    public WzSprite? ItemCategory(int index) => Get($"ItemCategory/{index}");
    public WzSprite? Cash => Get("cash");
    public WzSprite? Mesos => Get("mesos");
    public WzSprite? Star => Get("Star/Star");

    /// <summary>Width that <see cref="DrawNumber"/> will consume for <paramref name="value"/>.
    /// Mirrors the v95 <c>draw_text_by_image</c> tail position — sum of digit widths plus the
    /// inter-digit space.</summary>
    public int MeasureNumber(int value, bool met, int horzSpace = 0)
    {
        var w = 0;
        var first = true;
        foreach (var ch in Stringify(value))
        {
            var s = Digit(ch - '0', met);
            if (s is null) continue;
            if (!first) w += horzSpace;
            w += s.Width;
            first = false;
        }
        return w;
    }

    /// <summary>Compose <paramref name="value"/> from bitmap digits left-to-right starting at
    /// (<paramref name="x"/>, <paramref name="y"/>). Returns the pen-x after the last digit so
    /// callers can place a trailing "%" / "Max" tag without re-measuring. Mirrors v95
    /// <c>draw_number_by_image</c>; each digit canvas's origin is honoured (digit "1" has
    /// <c>origin=(-1,0)</c>, "none" dash has <c>origin=(0,-3)</c>).</summary>
    public int DrawNumber(SpriteBatch sb, int x, int y, int value, bool met, int horzSpace = 0)
    {
        var penX = x;
        var first = true;
        foreach (var ch in Stringify(value))
        {
            var s = Digit(ch - '0', met);
            if (s is null) continue;
            if (!first) penX += horzSpace;
            sb.Draw(s.Texture, new Vector2(penX - s.Origin.X, y - s.Origin.Y), Color.White);
            penX += s.Width;
            first = false;
        }
        return penX;
    }

    /// <summary>Blit a single sprite at (<paramref name="x"/>, <paramref name="y"/>) respecting
    /// its origin offset. Returns the pen-x after the sprite for convenient chaining.</summary>
    public static int BlitAt(SpriteBatch sb, WzSprite? s, int x, int y)
    {
        if (s is null) return x;
        sb.Draw(s.Texture, new Vector2(x - s.Origin.X, y - s.Origin.Y), Color.White);
        return x + s.Width;
    }

    private static string Stringify(int v) =>
        v.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
