using MapleClaude.Wz;
using Microsoft.Extensions.Logging;

namespace MapleClaude.Character;

/// <summary>
/// The global avatar layer draw order from <c>Base.wz/zmap.img</c>. The image's
/// children are an <b>ordered</b> list of z-names, front-most first (e.g.
/// <c>...,&#160;hair,&#160;face,&#160;head,&#160;...,&#160;body,&#160;...,&#160;hairBelowBody,&#160;...</c>).
/// Each avatar part canvas carries a <c>z</c> string; sorting parts by
/// <see cref="FrontIndex"/> <b>descending</b> draws them back-to-front correctly.
/// </summary>
public sealed class AvatarZMap
{
    private readonly Dictionary<string, int> _index = new(StringComparer.Ordinal);

    public AvatarZMap(WzPackage? baseWz, ILogger logger)
    {
        if (baseWz?.GetItem("zmap.img") is WzImage img)
        {
            var i = 0;
            foreach (var (key, _) in img.Root.Items)
            {
                _index[key] = i++;
            }
        }
        if (_index.Count == 0)
        {
            logger.LogWarning("Base.wz/zmap.img not loaded — using the fallback avatar layer order");
            var i = 0;
            foreach (var z in Fallback)
            {
                _index[z] = i++;
            }
        }
    }

    /// <summary>Position in the front→back z list. Lower = more in front. Unknown names
    /// sort behind everything (so unexpected layers don't cover the avatar).</summary>
    public int FrontIndex(string? z) =>
        z is not null && _index.TryGetValue(z, out var v) ? v : int.MaxValue;

    // Minimal front→back fallback (only used if Base.wz/zmap.img is unavailable).
    private static readonly string[] Fallback =
    [
        "weaponOverGlove", "gloveOverHair", "handOverHair", "weaponOverHand", "weaponOverArm",
        "weaponBelowArm", "capOverHair", "hairOverHead", "cap", "hair", "accessoryFace", "face",
        "hairShade", "backHair", "head", "cape", "mailArm", "glove", "hand", "arm", "weapon",
        "mailArmBelowHead", "armBelowHead", "weaponOverBody", "pantsOverMailChest", "mailChest",
        "shoesOverPants", "pants", "shoes", "gloveOverBody", "body", "capeBelowBody",
        "hairBelowBody", "backHairBelowCap", "weaponBelowBody",
    ];
}
