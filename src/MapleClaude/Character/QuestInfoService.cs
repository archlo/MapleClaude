using MapleClaude.Wz;
using Microsoft.Extensions.Logging;

namespace MapleClaude.Character;

/// <summary>Start or complete requirements for a quest (Quest.wz/Check.img/&lt;id&gt;/{0|1}). Mirrors
/// the upstream Kinoko <c>QuestInfo.resolveQuestChecks</c> field set in
/// <c>provider/quest/QuestInfo.java</c>.</summary>
public sealed class QuestReq
{
    public int Npc;                                    // start/complete NPC template id (0 = none)
    public int LvMin, LvMax;                           // 0 = no bound
    public List<int> Jobs = new();                     // job-check entries; empty = any job
    public List<(int Id, int State)> Quests = new();   // prerequisite quests + required state
    public List<(int Id, int Count)> Items = new();
    public List<(int Id, int Count)> Mobs = new();
    public List<(int Id, int Level)> Skills = new();   // QuestSkillCheck: required skill id + min lv
    public int SubJobFlags;                            // QuestSubJobCheck mask (0 = any)
    public int Morph;                                  // QuestMorphCheck: required active morph (0 = any)
    public int Buff;                                   // QuestBuffCheck (must have this buff item id)
    public int ExceptBuff;                             // QuestBuffCheck except (must NOT have this)
    public DateTime? StartDate, EndDate;               // QuestDateCheck (UTC, parsed from "yyyy-MM-dd HH:mm")
    public int DayOfWeekMask;                          // QuestDayOfWeekCheck: bit i = (allowed on (DayOfWeek)i)
    public int InfoExQuestId;                          // infoNumber override for infoex lookup (0 = self)
    public List<(int Index, string Value)> InfoEx = new(); // QuestExCheck: (questrecord-info index, expected string)
}

/// <summary>One quest's WZ data: display text (Quest.wz/QuestInfo.img/&lt;id&gt;) plus start/complete
/// checks (Check.img/&lt;id&gt;/{0,1}). Mirrors the fields the upstream Kinoko QuestProvider reads, plus
/// the client-only <c>npc</c> and prerequisite-<c>quest</c> fields the server ignores but the client
/// needs to place NPC quest markers and compute availability.</summary>
public sealed class QuestData
{
    public int Id;
    public string Name = string.Empty;
    public string Parent = string.Empty;
    public int Area;
    public int Order;
    public bool AutoStart, AutoComplete;
    public string Summary = string.Empty;
    public string DemandSummary = string.Empty;
    public string RewardSummary = string.Empty;
    public string[] Blurb = ["", "", ""];   // 0 = available hint, 1 = in-progress, 2 = complete
    public QuestReq Start = new();
    public QuestReq Complete = new();
}

/// <summary>
/// Reads Quest.wz into <see cref="QuestData"/> records and an NPC→quests index, mirroring the
/// upstream Kinoko QuestProvider layout (<c>QuestInfo.img</c> + <c>Check.img/&lt;id&gt;/{0,1}</c>).
/// Loaded once on first use and cached. The WZ package is supplied via a delegate (it opens after
/// construction). Patterned on <see cref="SkillInfoService"/>.
/// </summary>
public sealed class QuestInfoService
{
    private static readonly IReadOnlyList<(int QuestId, bool IsStart)> Empty = Array.Empty<(int, bool)>();

    private readonly Func<WzPackage?> _questWz;
    private readonly ILogger? _logger;
    private Dictionary<int, QuestData>? _all;
    private Dictionary<int, List<(int QuestId, bool IsStart)>>? _byNpc;

    public QuestInfoService(Func<WzPackage?> questWz, ILogger? logger = null)
    {
        _questWz = questWz;
        _logger = logger;
    }

    public QuestData? Get(int questId)
    {
        EnsureLoaded();
        return _all?.GetValueOrDefault(questId);
    }

    public IReadOnlyDictionary<int, QuestData> All()
    {
        EnsureLoaded();
        return _all ?? (IReadOnlyDictionary<int, QuestData>)new Dictionary<int, QuestData>();
    }

    /// <summary>Quests whose start NPC (<c>IsStart=true</c>) or complete NPC is this template id.</summary>
    public IReadOnlyList<(int QuestId, bool IsStart)> ForNpc(int npcTemplateId)
    {
        EnsureLoaded();
        return _byNpc?.GetValueOrDefault(npcTemplateId) ?? Empty;
    }

    private void EnsureLoaded()
    {
        if (_all != null) return;
        var wz = _questWz();
        if (wz is null) return;   // Quest.wz not open yet — retry on a later call (don't cache empty)

        var all = new Dictionary<int, QuestData>();
        var byNpc = new Dictionary<int, List<(int, bool)>>();
        try
        {
            var qiRoot = (wz.GetItem("QuestInfo.img") as WzImage)?.Root;
            var ckRoot = (wz.GetItem("Check.img") as WzImage)?.Root;
            if (qiRoot is null) { _all = all; _byNpc = byNpc; return; }

            foreach (var (key, val) in qiRoot.Items)
            {
                if (!int.TryParse(key, out var id) || val is not WzProperty qi) continue;
                var q = new QuestData { Id = id };
                ParseInfo(q, qi);
                if (ckRoot?.Get(key) is WzProperty ck)
                {
                    q.Start    = ParseReq(ck.Get("0") as WzProperty);
                    q.Complete = ParseReq(ck.Get("1") as WzProperty);
                }
                all[id] = q;
                if (q.Start.Npc != 0)    Index(byNpc, q.Start.Npc, id, true);
                if (q.Complete.Npc != 0) Index(byNpc, q.Complete.Npc, id, false);
            }
            _logger?.LogInformation("QuestInfoService: loaded {Count} quests", all.Count);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "QuestInfoService: failed loading Quest.wz");
        }
        _all = all;
        _byNpc = byNpc;
    }

    private static void Index(Dictionary<int, List<(int, bool)>> map, int npc, int questId, bool isStart)
    {
        if (!map.TryGetValue(npc, out var list)) map[npc] = list = new();
        list.Add((questId, isStart));
    }

    private static void ParseInfo(QuestData q, WzProperty p)
    {
        q.Name          = Str(p.Get("name"));
        q.Parent        = Str(p.Get("parent"));
        q.Area          = I(p.Get("area"));
        q.Order         = I(p.Get("order"));
        q.AutoStart     = I(p.Get("autoStart")) != 0;
        q.AutoComplete  = I(p.Get("autoComplete")) != 0;
        q.Summary       = Str(p.Get("summary"));
        q.DemandSummary = Str(p.Get("demandSummary"));
        q.RewardSummary = Str(p.Get("rewardSummary"));
        q.Blurb[0]      = Str(p.Get("0"));
        q.Blurb[1]      = Str(p.Get("1"));
        q.Blurb[2]      = Str(p.Get("2"));
    }

    private static QuestReq ParseReq(WzProperty? p)
    {
        var r = new QuestReq();
        if (p is null) return r;
        r.Npc   = I(p.Get("npc"));
        r.LvMin = I(p.Get("lvmin"));
        r.LvMax = I(p.Get("lvmax"));
        r.SubJobFlags = I(p.Get("subJobFlags"));
        r.Morph       = I(p.Get("morph"));
        r.Buff        = I(p.Get("buff"));
        r.ExceptBuff  = I(p.Get("exceptbuff"));
        r.InfoExQuestId = I(p.Get("infoNumber"));
        r.StartDate = ParseQuestDate(Str(p.Get("start")));
        r.EndDate   = ParseQuestDate(Str(p.Get("end")));
        if (p.Get("job") is WzProperty jobs)
            foreach (var (_, v) in jobs.Items) r.Jobs.Add(I(v));
        if (p.Get("quest") is WzProperty quests)
            foreach (var (_, v) in quests.Items)
                if (v is WzProperty qq) r.Quests.Add((I(qq.Get("id")), I(qq.Get("state"))));
        if (p.Get("item") is WzProperty items)
            foreach (var (_, v) in items.Items)
                if (v is WzProperty it) r.Items.Add((I(it.Get("id")), I(it.Get("count"))));
        if (p.Get("mob") is WzProperty mobs)
            foreach (var (_, v) in mobs.Items)
                if (v is WzProperty mo) r.Mobs.Add((I(mo.Get("id")), I(mo.Get("count"))));
        if (p.Get("skill") is WzProperty skills)
            foreach (var (_, v) in skills.Items)
                if (v is WzProperty sk) r.Skills.Add((I(sk.Get("id")), I(sk.Get("acquire"))));
        if (p.Get("dayOfWeek") is WzProperty dow)
            foreach (var (_, v) in dow.Items)
            {
                var day = I(v);
                if (day is >= 0 and <= 6) r.DayOfWeekMask |= 1 << day;
            }
        if (p.Get("infoex") is WzProperty ex)
            foreach (var (key, v) in ex.Items)
                r.InfoEx.Add((int.TryParse(key, out var k) ? k : 0, Str(v)));
        return r;
    }

    // Quest.wz dates are stored as "yyyyMMddHHmm" (12 chars; e.g. "200807300000"). Kinoko's
    // QuestDateCheck.from parses with that format. Treat anything malformed as "no bound".
    private static DateTime? ParseQuestDate(string s)
    {
        if (string.IsNullOrEmpty(s) || s.Length < 8) return null;
        return DateTime.TryParseExact(s.PadRight(12, '0'), "yyyyMMddHHmm",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
            out var dt) ? dt : null;
    }

    private static int I(object? v) => v switch
    {
        int i => i, short s => s, long l => (int)l, byte b => b,
        _ => 0,
    };

    private static string Str(object? v) => v as string ?? string.Empty;
}
