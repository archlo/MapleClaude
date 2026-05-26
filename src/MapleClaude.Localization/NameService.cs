using MapleClaude.Wz;
using Microsoft.Extensions.Logging;

namespace MapleClaude.Localization;

/// <summary>
/// Resolves display names from <c>String.wz</c>, mirroring the upstream Kinoko
/// <c>StringProvider</c> layout. Each category (items, maps, mobs, npcs, skills)
/// is loaded lazily on first use and cached. Lookups return <c>null</c> when a
/// name is absent so callers can fall back to a placeholder.
///
/// The WZ package is supplied through a delegate because it opens after this
/// service is constructed (the package handle is resolved on first category load).
/// </summary>
public sealed class NameService
{
    // String.wz/Eqp.img/Eqp/<Type>/<id>/name — the equip-name sub-categories.
    private static readonly string[] EquipTypes =
    [
        "Accessory", "Cap", "Cape", "Coat", "Dragon", "Face", "Glove", "Hair",
        "Longcoat", "Mechanic", "Pants", "PetEquip", "Ring", "Shield", "Shoes",
        "Taming", "Weapon",
    ];

    private readonly Func<WzPackage?> _stringWz;
    private readonly Func<WzPackage?>? _questWz;
    private readonly ILogger? _logger;

    private Dictionary<int, string>? _items;
    private Dictionary<int, string>? _itemDescs;
    private Dictionary<int, string>? _quests;
    private Dictionary<int, string>? _maps;
    private Dictionary<int, string>? _mobs;
    private Dictionary<int, string>? _npcs;
    private Dictionary<int, Dictionary<string, string>>? _npcStrings;
    private Dictionary<int, string>? _skills;

    public NameService(Func<WzPackage?> stringWzProvider, ILogger? logger = null,
                       Func<WzPackage?>? questWzProvider = null)
    {
        _stringWz = stringWzProvider;
        _questWz = questWzProvider;
        _logger = logger;
    }

    public string? ItemName(int id)  => Items().GetValueOrDefault(id);
    /// <summary>The item's flavour-text description (String.wz <c>…/&lt;id&gt;/desc</c>), or null.</summary>
    public string? ItemDesc(int id)  => ItemDescs().GetValueOrDefault(id);
    public string? SkillName(int id) => Skills().GetValueOrDefault(id);
    public string? MapName(int id)   => Maps().GetValueOrDefault(id);
    public string? MobName(int id)   => Mobs().GetValueOrDefault(id);
    public string? NpcName(int id)   => Npcs().GetValueOrDefault(id);
    public string? QuestName(int id) => Quests().GetValueOrDefault(id);

    /// <summary>A single String.wz/Npc.img/&lt;id&gt;/&lt;key&gt; string (e.g. an ambient-speak line
    /// keyed "n0"/"s0"), or null. Used to resolve <see cref="MapleClaude.Wz"/> NPC <c>info/speak</c> keys.</summary>
    public string? NpcText(int id, string key) => NpcStrings().GetValueOrDefault(id)?.GetValueOrDefault(key);

    // ── Lazy category loaders ───────────────────────────────────────────────────

    private Dictionary<int, string> Items()     => _items     ??= LoadItemStrings("name");
    private Dictionary<int, string> ItemDescs() => _itemDescs ??= LoadItemStrings("desc");
    private Dictionary<int, string> Maps()   => _maps   ??= LoadMaps();
    private Dictionary<int, string> Mobs()   => _mobs   ??= LoadFlatImage("Mob.img");
    private Dictionary<int, string> Npcs()   => _npcs   ??= LoadNpcs();
    private Dictionary<int, Dictionary<string, string>> NpcStrings() => _npcStrings ??= LoadNpcStrings();
    private Dictionary<int, string> Skills() => _skills ??= LoadSkills();
    private Dictionary<int, string> Quests() => _quests ??= LoadQuests();

    // Quest.wz/QuestInfo.img/<id>/name.
    private Dictionary<int, string> LoadQuests()
    {
        var dict = new Dictionary<int, string>();
        var wz = _questWz?.Invoke();
        if (wz?.GetItem("QuestInfo.img") is not WzImage image) return dict;
        try
        {
            AddNames(image.Root, dict);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "NameService: failed loading quest names");
        }
        return dict;
    }

    // Loads either the item names (fieldKey "name") or descriptions (fieldKey "desc"). Same WZ layout:
    // Eqp.img/Eqp/<Type>/<id>/<field>, Etc.img/Etc/<id>/<field>, and flat Consume/Ins/Cash/Pet images.
    private Dictionary<int, string> LoadItemStrings(string fieldKey)
    {
        var dict = new Dictionary<int, string>();
        var wz = _stringWz();
        if (wz is null) return dict;
        try
        {
            if (wz.GetItem("Eqp.img/Eqp") is WzProperty eqp)
            {
                foreach (var type in EquipTypes)
                {
                    if (eqp.Get(type) is WzProperty list) AddNames(list, dict, fieldKey);
                }
            }
            if (wz.GetItem("Etc.img/Etc") is WzProperty etc) AddNames(etc, dict, fieldKey);
            foreach (var img in new[] { "Consume.img", "Ins.img", "Cash.img", "Pet.img" })
            {
                if (wz.GetItem(img) is WzImage image) AddNames(image.Root, dict, fieldKey);
            }
            _logger?.LogInformation("NameService: loaded {Count} item {Field} strings", dict.Count, fieldKey);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "NameService: failed loading item {Field} strings", fieldKey);
        }
        return dict;
    }

    private Dictionary<int, string> LoadMaps()
    {
        var dict = new Dictionary<int, string>();
        var wz = _stringWz();
        if (wz?.GetItem("Map.img") is not WzImage image) return dict;
        try
        {
            // Map.img/<type>/<mapId>/{streetName, mapName}
            foreach (var (_, typeVal) in image.Root.Items)
            {
                if (typeVal is not WzProperty mapList) continue;
                foreach (var (key, val) in mapList.Items)
                {
                    if (!int.TryParse(key, out var id) || val is not WzProperty p) continue;
                    var street = p.Get("streetName") as string ?? string.Empty;
                    var name   = p.Get("mapName") as string ?? string.Empty;
                    dict[id] = street.Length > 0 ? $"{street} : {name}" : name;
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "NameService: failed loading map names");
        }
        return dict;
    }

    private Dictionary<int, string> LoadNpcs()
    {
        var dict = new Dictionary<int, string>();
        var wz = _stringWz();
        if (wz?.GetItem("Npc.img") is not WzImage image) return dict;
        try
        {
            // Npc.img/<id>/{name, func}
            foreach (var (key, val) in image.Root.Items)
            {
                if (!int.TryParse(key, out var id) || val is not WzProperty p) continue;
                if (p.Get("name") is not string name) continue;
                dict[id] = p.Get("func") is string func ? $"{name} : {func}" : name;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "NameService: failed loading npc names");
        }
        return dict;
    }

    // String.wz/Npc.img/<id>/* — the full per-NPC string sub-table (name, func, d0…, n0…/s0…),
    // cached so ambient-speak keys resolve without re-walking the WZ each tick.
    private Dictionary<int, Dictionary<string, string>> LoadNpcStrings()
    {
        var dict = new Dictionary<int, Dictionary<string, string>>();
        var wz = _stringWz();
        if (wz?.GetItem("Npc.img") is not WzImage image) return dict;
        try
        {
            foreach (var (key, val) in image.Root.Items)
            {
                if (!int.TryParse(key, out var id) || val is not WzProperty p) continue;
                var sub = new Dictionary<string, string>();
                foreach (var (k, v) in p.Items)
                    if (v is string s) sub[k] = s;
                if (sub.Count > 0) dict[id] = sub;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "NameService: failed loading npc strings");
        }
        return dict;
    }

    private Dictionary<int, string> LoadSkills()
    {
        var dict = new Dictionary<int, string>();
        var wz = _stringWz();
        if (wz?.GetItem("Skill.img") is not WzImage image) return dict;
        try
        {
            // Skill.img/<skillId>/name (skill ids are >= 7 digits)
            foreach (var (key, val) in image.Root.Items)
            {
                if (key.Length < 7 || !int.TryParse(key, out var id) || val is not WzProperty p) continue;
                if (p.Get("name") is string name) dict[id] = name;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "NameService: failed loading skill names");
        }
        return dict;
    }

    // Generic <img>/<id>/name loader (used for Mob.img).
    private Dictionary<int, string> LoadFlatImage(string imagePath)
    {
        var dict = new Dictionary<int, string>();
        var wz = _stringWz();
        if (wz?.GetItem(imagePath) is not WzImage image) return dict;
        try
        {
            AddNames(image.Root, dict);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "NameService: failed loading names from {Image}", imagePath);
        }
        return dict;
    }

    // Add every "<id>/<key>" string under the container to the dictionary.
    private static void AddNames(WzProperty container, Dictionary<int, string> dict, string key = "name")
    {
        foreach (var (k, v) in container.Items)
        {
            if (int.TryParse(k, out var id) && v is WzProperty p && p.Get(key) is string name)
            {
                dict[id] = name;
            }
        }
    }
}
