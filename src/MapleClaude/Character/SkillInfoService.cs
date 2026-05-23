using MapleClaude.Wz;
using Microsoft.Extensions.Logging;

namespace MapleClaude.Character;

/// <summary>Per-skill data read from Skill.wz: max level, passive flag, icon, and
/// per-level MP cost / cooldown / buff duration. Index 0 of each array is level 1.</summary>
public sealed class SkillInfo
{
    public int MaxLevel = 1;
    public bool Passive;
    public WzCanvas? Icon;
    public int[] MpCon = [];
    public int[] Cooltime = [];   // seconds
    public int[] BuffTime = [];   // seconds

    public int MpConAt(int level)    => At(MpCon, level);
    public int CooltimeAt(int level) => At(Cooltime, level);
    public int BuffTimeAt(int level) => At(BuffTime, level);

    private static int At(int[] a, int level) => level >= 1 && level <= a.Length ? a[level - 1] : 0;
}

/// <summary>
/// Resolves <see cref="SkillInfo"/> from Skill.wz, mirroring the upstream Kinoko
/// SkillProvider layout (<c>&lt;jobId&gt;.img/skill/&lt;skillId&gt;</c>, where
/// jobId = skillId / 10000). Results are cached; a missing skill returns null.
/// The WZ package is supplied via a delegate (it opens after construction).
/// </summary>
public sealed class SkillInfoService
{
    private readonly Func<WzPackage?> _skillWz;
    private readonly ILogger? _logger;
    private readonly Dictionary<int, SkillInfo?> _cache = new();

    public SkillInfoService(Func<WzPackage?> skillWz, ILogger? logger = null)
    {
        _skillWz = skillWz;
        _logger = logger;
    }

    public SkillInfo? Get(int skillId)
    {
        if (_cache.TryGetValue(skillId, out var cached)) return cached;
        SkillInfo? info = null;
        try
        {
            info = Load(skillId);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "SkillInfo load failed for {SkillId}", skillId);
        }
        _cache[skillId] = info;
        return info;
    }

    private SkillInfo? Load(int skillId)
    {
        var wz = _skillWz();
        if (wz is null) return null;
        var jobImg = skillId / 10000;
        if (wz.GetItem($"{jobImg}.img/skill/{skillId}") is not WzProperty node) return null;

        var info = new SkillInfo
        {
            Passive = ReadInt(node.Get("psd")) != 0,
            Icon = node.Get("icon") as WzCanvas,
        };

        if (node.Get("level") is WzProperty levels)
        {
            var n = levels.Items.Count;
            info.MaxLevel = Math.Max(1, n);
            var mp = new int[n];
            var cd = new int[n];
            var bt = new int[n];
            for (var lv = 1; lv <= n; lv++)
            {
                if (levels.Get(lv.ToString(System.Globalization.CultureInfo.InvariantCulture)) is not WzProperty lp) continue;
                mp[lv - 1] = ReadInt(lp.Get("mpCon"));
                cd[lv - 1] = ReadInt(lp.Get("cooltime"));
                bt[lv - 1] = ReadInt(lp.Get("time"));
            }
            info.MpCon = mp;
            info.Cooltime = cd;
            info.BuffTime = bt;
        }
        else if (node.Get("common") is WzProperty common)
        {
            // Computed (formula) skill — values are expressions; we only take maxLevel.
            info.MaxLevel = Math.Max(1, ReadInt(common.Get("maxLevel")));
        }

        return info;
    }

    private static int ReadInt(object? v) => v switch
    {
        int i => i,
        short s => s,
        long l => (int)l,
        string s when int.TryParse(s, out var n) => n,
        _ => 0,
    };
}
