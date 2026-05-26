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

/// <summary>Cast-time animation data for a skill: the body action(s) the caster
/// performs (one chosen at random) plus the raw WZ nodes for the <c>effect</c> /
/// <c>screen</c> / <c>hit</c> animations (fed to <c>WzTextureLoader.LoadAnimation</c>).</summary>
public sealed class SkillCastInfo
{
    public string[] Actions = [];
    public object? Effect;
    public object? Effect0;
    public object? Screen;
    public object? Hit;
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
    private readonly Dictionary<int, IReadOnlyList<int>> _idsByRoot = new();
    private readonly Dictionary<int, WzCanvas?> _bookIcons = new();
    private static readonly System.Globalization.CultureInfo Inv = System.Globalization.CultureInfo.InvariantCulture;
    private readonly Dictionary<int, SkillCastInfo?> _castCache = new();

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

    /// <summary>
    /// Every skill id defined under <c>Skill.wz/{root}.img/skill</c>, sorted
    /// ascending, with skills flagged <c>invisible</c> filtered out (mirroring the
    /// client's skill-book list). Cached per root. Returns empty — <em>without
    /// caching</em> — when Skill.wz isn't open yet (it loads lazily after ctor).
    /// </summary>
    public IReadOnlyList<int> EnumerateSkillIds(int root)
    {
        if (_idsByRoot.TryGetValue(root, out var cached)) return cached;
        var wz = _skillWz();
        if (wz is null) return Array.Empty<int>();
        try
        {
            if (JobItem(wz, root, "skill") is not WzProperty skills) return Array.Empty<int>();
            var ids = new List<int>();
            foreach (var (key, value) in skills.Items)
            {
                if (!int.TryParse(key, System.Globalization.NumberStyles.Integer,
                        System.Globalization.CultureInfo.InvariantCulture, out var id)) continue;
                if (value is WzProperty sp && ReadInt(sp.Get("invisible")) != 0) continue;
                ids.Add(id);
            }
            ids.Sort();
            _idsByRoot[root] = ids;
            return ids;
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "EnumerateSkillIds failed for root {Root}", root);
            return Array.Empty<int>();
        }
    }

    /// <summary>The skill-book icon for a job root (<c>Skill.wz/{root}.img/info/icon</c>),
    /// shown on the Skill window's job tabs. Cached (including null) once Skill.wz is open.</summary>
    public WzCanvas? GetBookIcon(int root)
    {
        if (_bookIcons.TryGetValue(root, out var cached)) return cached;
        var wz = _skillWz();
        if (wz is null) return null; // don't cache; Skill.wz opens lazily
        WzCanvas? icon = null;
        try
        {
            icon = JobItem(wz, root, "info/icon") as WzCanvas;
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "GetBookIcon failed for root {Root}", root);
        }
        _bookIcons[root] = icon;
        return icon;
    }

    /// <summary>Cast-time animation data (action list + effect/screen/hit nodes) for a
    /// skill, cached. Returns null when the skill is missing or Skill.wz isn't open yet.</summary>
    public SkillCastInfo? GetCastInfo(int skillId)
    {
        if (_castCache.TryGetValue(skillId, out var cached)) return cached;
        var wz = _skillWz();
        if (wz is null) return null; // don't cache; Skill.wz opens lazily
        SkillCastInfo? info = null;
        try
        {
            if (SkillNode(wz, skillId) is { } node)
            {
                info = new SkillCastInfo
                {
                    Actions = ReadActions(node.Get("action") as WzProperty),
                    Effect  = node.Get("effect"),
                    Effect0 = node.Get("effect0"),
                    Screen  = node.Get("screen"),
                    Hit     = node.Get("hit"),
                };
            }
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "GetCastInfo failed for {SkillId}", skillId);
        }
        _castCache[skillId] = info;
        return info;
    }

    private static string[] ReadActions(WzProperty? action)
    {
        if (action is null) return [];
        var list = new List<string>();
        foreach (var (_, v) in action.Items)
            if (v is string s && s.Length > 0) list.Add(s);
        return list.ToArray();
    }

    private SkillInfo? Load(int skillId)
    {
        var wz = _skillWz();
        if (wz is null) return null;
        if (SkillNode(wz, skillId) is not WzProperty node) return null;

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

    // Skill.wz job images are zero-padded to 3 digits (000.img) and skill nodes to 7
    // (0001003) in GMS v95; fall back to the unpadded names for other dumps.
    private static string D3(int n) => n.ToString("D3", Inv);
    private static string D7(int n) => n.ToString("D7", Inv);

    private static object? JobItem(WzPackage wz, int job, string tail) =>
        wz.GetItem($"{D3(job)}.img/{tail}") ?? wz.GetItem($"{job.ToString(Inv)}.img/{tail}");

    private static WzProperty? SkillNode(WzPackage wz, int skillId)
    {
        var job = skillId / 10000;
        return (wz.GetItem($"{D3(job)}.img/skill/{D7(skillId)}") as WzProperty)
            ?? (wz.GetItem($"{job.ToString(Inv)}.img/skill/{skillId.ToString(Inv)}") as WzProperty);
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
