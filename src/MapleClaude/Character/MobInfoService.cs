using MapleClaude.Wz;

namespace MapleClaude.Character;

/// <summary>
/// Lazily parses + caches one <see cref="MobInfo"/> per Mob.wz template id. Resolves
/// the standard <c>info/link</c> redirect (variants point at a canonical template's
/// info + animation) the same way <see cref="MobLook.Load"/> does.
/// </summary>
public sealed class MobInfoService
{
    private readonly WzPackage? _mobWz;
    private readonly Dictionary<int, MobInfo> _cache = new();

    public MobInfoService(WzPackage? mobWz) { _mobWz = mobWz; }

    /// <summary>Returns the (cached) info for a template id. Never null — falls back to
    /// a defaulted <see cref="MobInfo"/> (walk, no aggro) when the template is missing,
    /// so the controller can still drive a generic walker without throwing.</summary>
    public MobInfo Get(int templateId)
    {
        if (_cache.TryGetValue(templateId, out var hit)) return hit;
        var info = Parse(templateId) ?? new MobInfo { TemplateId = templateId };
        _cache[templateId] = info;
        return info;
    }

    private MobInfo? Parse(int templateId)
    {
        if (_mobWz is null) return null;
        if (_mobWz.GetItem($"{templateId:D7}.img") is not WzImage img) return null;
        var root = img.Root;

        // info/link redirects (e.g. variant recolours share a canonical info block).
        if (root.GetItem("info/link") is string link && int.TryParse(link, out var linkId)
            && _mobWz.GetItem($"{linkId:D7}.img") is WzImage linked)
        {
            root = linked.Root;
        }

        if (root.GetItem("info") is not WzProperty info) return null;

        var flySpeed = ReadInt(info, "flySpeed");
        if (flySpeed == 0) flySpeed = ReadInt(info, "fs");

        return new MobInfo
        {
            TemplateId  = templateId,
            MoveAbility = ReadInt(info, "moveAbility", 1),
            Speed       = ReadInt(info, "speed"),
            FlySpeed    = flySpeed,
            Fly         = ReadBool(info, "fly"),
            FirstAttack = ReadBool(info, "firstAttack"),
            BodyAttack  = ReadBool(info, "bodyAttack"),
            NoFlip      = ReadBool(info, "noFlip"),
            Pushed      = ReadInt(info, "pushed"),
            Boss        = ReadBool(info, "boss"),
            Level       = ReadInt(info, "level", 1),
        };
    }

    // Mob.wz numeric leaves come back as int/short/long depending on the original WZ
    // encoding; coerce all of them to int (matches the MobLook.ReadDelay pattern).
    private static int ReadInt(WzProperty p, string key, int def = 0) => p.Get(key) switch
    {
        int i   => i,
        short s => s,
        long l  => (int)l,
        _       => def,
    };

    private static bool ReadBool(WzProperty p, string key) => ReadInt(p, key) != 0;
}
