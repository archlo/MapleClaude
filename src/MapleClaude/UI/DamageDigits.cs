using MapleClaude.Render;
using MapleClaude.Wz;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace MapleClaude.UI;

/// <summary>
/// Loads the default "damage skin" sprite digits from <c>Effect.wz/BasicEff.img</c> and
/// renders multi-digit numbers — used by <see cref="MapleClaude.Character.DamageNumber"/>
/// to draw the floating "damage you dealt" / Miss / Critical numbers above mobs.
///
/// <para>WZ paths used (per v95 layout, confirmed via the localization string table):</para>
/// <list type="bullet">
///  <item><c>Effect/BasicEff.img/NoRed1/0..9</c> — normal-damage digits (white outlined,
///        what the player sees when WE hit a mob).</item>
///  <item><c>Effect/BasicEff.img/NoCri1/0..9</c> — critical-damage digits (orange/yellow).</item>
///  <item><c>Effect/BasicEff.img/NoMiss/0</c> — the "MISS" sprite.</item>
/// </list>
///
/// <para>Per-build node names sometimes vary (NoRed0 vs NoRed1 etc.), so each set tries a
/// few candidate paths in order — the first one that loads digit 0 wins. If nothing
/// loads, <see cref="LoadedWhite"/>/<see cref="LoadedCrit"/>/<see cref="LoadedMiss"/>
/// stay false and callers fall back to text rendering.</para>
/// </summary>
public sealed class DamageDigits
{
    private readonly WzSprite?[] _white = new WzSprite?[10];
    private readonly WzSprite?[] _crit  = new WzSprite?[10];
    private WzSprite? _miss;

    private const int DigitOverlap = 2;        // px overlap between adjacent digits (GMS feel)
    private const string BasicEff  = "BasicEff.img";

    public bool LoadedWhite => _white[0] is not null;
    public bool LoadedCrit  => _crit[0]  is not null;
    public bool LoadedMiss  => _miss     is not null;

    public DamageDigits(WzPackage? effectWz, WzTextureLoader loader)
    {
        if (effectWz is null) return;

        // Try the standard candidates in order. v95 builds vary slightly between region
        // / patch on which digit set is the "default" — NoRed1 is the most common for
        // damage we deal to mobs, with NoRed0 as a fallback.
        foreach (var node in new[] { "NoRed1", "NoRed0", "Basic" })
        {
            if (TryLoadDigits(effectWz, loader, $"{BasicEff}/{node}", _white))
            {
                break;
            }
        }
        foreach (var node in new[] { "NoCri1", "NoCri0", "Cri" })
        {
            if (TryLoadDigits(effectWz, loader, $"{BasicEff}/{node}", _crit))
            {
                break;
            }
        }
        foreach (var path in new[] { $"{BasicEff}/NoMiss/0", $"{BasicEff}/Miss/0" })
        {
            if (effectWz.GetItem(path) is WzCanvas mc)
            {
                _miss = loader.Load(mc);
                break;
            }
        }
    }

    /// <summary>Render a digit string (e.g. "123") centred on <paramref name="screenCenter"/>
    /// with the given <paramref name="alpha"/>. Use <paramref name="crit"/>=true for
    /// critical hits (loads the NoCri sprite set). Returns true when the sprites rendered;
    /// false when the requested set isn't loaded (caller should fall back to text).</summary>
    public bool DrawNumber(SpriteBatch sb, string text, Vector2 screenCenter, byte alpha, bool crit)
    {
        var set = crit ? _crit : _white;
        if (set[0] is null) return false;   // sprite set unavailable

        // Pass 1: total width = sum(digit.Width) - (count-1)*overlap.
        var totalW = 0;
        var count  = 0;
        foreach (var ch in text)
        {
            if (ch < '0' || ch > '9') continue;
            var sp = set[ch - '0'];
            if (sp is null) return false;   // a glyph missing — abort to text fallback
            totalW += sp.Width;
            count++;
        }
        if (count == 0) return false;
        totalW -= (count - 1) * DigitOverlap;

        var x = (float)System.Math.Round(screenCenter.X - totalW / 2f);
        var y = (float)System.Math.Round(screenCenter.Y);
        var tint = new Color((byte)255, (byte)255, (byte)255, alpha);
        foreach (var ch in text)
        {
            if (ch < '0' || ch > '9') continue;
            var sp = set[ch - '0']!;
            // WzSprite.Draw subtracts the sprite's Origin from position, so passing the
            // raw top-left target X (relative to the digit's logical anchor) is correct.
            sp.Draw(sb, new Vector2(x + sp.Origin.X, y + sp.Origin.Y), tint);
            x += sp.Width - DigitOverlap;
        }
        return true;
    }

    /// <summary>Render the "MISS" sprite centred at <paramref name="screenCenter"/>.
    /// Returns true on success, false if the sprite isn't loaded.</summary>
    public bool DrawMiss(SpriteBatch sb, Vector2 screenCenter, byte alpha)
    {
        if (_miss is null) return false;
        var tint = new Color((byte)255, (byte)255, (byte)255, alpha);
        var x = (float)System.Math.Round(screenCenter.X - _miss.Width / 2f);
        var y = (float)System.Math.Round(screenCenter.Y);
        _miss.Draw(sb, new Vector2(x + _miss.Origin.X, y + _miss.Origin.Y), tint);
        return true;
    }

    private static bool TryLoadDigits(WzPackage wz, WzTextureLoader loader, string basePath, WzSprite?[] dst)
    {
        // Walk 0..9 under basePath. Each may be a WzCanvas directly, or a sub-property
        // whose first WzCanvas child holds the bitmap (some WZ builds nest it).
        for (var i = 0; i < 10; i++)
        {
            var node = wz.GetItem($"{basePath}/{i}");
            if (node is WzCanvas c)
            {
                dst[i] = loader.Load(c);
            }
            else if (node is WzProperty p)
            {
                foreach (var (_, v) in p.Items)
                {
                    if (v is WzCanvas cc) { dst[i] = loader.Load(cc); break; }
                }
            }
        }
        return dst[0] is not null;
    }
}
