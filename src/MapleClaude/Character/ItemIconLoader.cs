using MapleClaude.Render;
using MapleClaude.Wz;

namespace MapleClaude.Character;

/// <summary>
/// Resolves an item's inventory icon (the 32×32-ish cell sprite) from the WZ files.
/// Equips (<c>1xxxxxx</c>) live in <c>Character.wz</c> at
/// <c>&lt;Category&gt;/&lt;itemId:D8&gt;.img/info/icon</c> — the same image that holds
/// the avatar sprite; the category folder is derived from the item-id prefix
/// (<c>itemId / 10000</c>), generalizing the inline folder logic in
/// <see cref="CharacterRenderer"/> to every wearable slot. Consumables, setup, etc and
/// cash items (<c>2..5xxxxxx</c>) live in <c>Item.wz</c> at
/// <c>&lt;Cat&gt;/&lt;itemId/10000:D4&gt;.img/&lt;itemId:D8&gt;/info/icon</c>.
///
/// Pendant/Belt/Medal and the face/eye/earring accessories all share the
/// <c>Accessory</c> folder in v95 (verified against the WZ tree); Rings, Shields,
/// Capes and Weapons have their own folders. Every lookup falls back from <c>icon</c>
/// to <c>iconRaw</c> and resolves UOL nodes. Loaded sprites (and misses) are cached
/// by item id so repeated draws never re-walk the WZ tree.
/// </summary>
public sealed class ItemIconLoader
{
    private readonly WzTextureLoader _loader;
    private readonly WzPackage? _characterWz;
    private readonly WzPackage? _itemWz;
    private readonly Dictionary<int, WzSprite?> _cache = new();
    private readonly Dictionary<int, ItemAttr?> _attrCache = new();

    public ItemIconLoader(WzTextureLoader loader, WzPackage? characterWz, WzPackage? itemWz = null)
    {
        _loader = loader;
        _characterWz = characterWz;
        _itemWz = itemWz;
    }

    /// <summary>The inventory icon for an item, or <c>null</c> if the id isn't a
    /// recognised item or the asset is missing (caller falls back to a placeholder).</summary>
    public WzSprite? LoadIcon(int itemId)
    {
        if (_cache.TryGetValue(itemId, out var cached))
        {
            return cached;
        }

        WzSprite? sprite = null;
        try
        {
            sprite = itemId / 1_000_000 == 1 ? LoadEquipIcon(itemId) : LoadItemIcon(itemId);
        }
        catch
        {
            sprite = null; // corrupt/absent node — degrade to the placeholder
        }

        _cache[itemId] = sprite; // cache misses too, so we don't re-walk every frame
        return sprite;
    }

    /// <summary>Parsed item attributes for the tooltip (requirements + bonuses), read from the
    /// item's <c>info</c> node. Equips → Character.wz; consumables/etc → Item.wz. Returns null when
    /// the item has no info node (caller shows just the name). Cached per id (misses too).</summary>
    public ItemAttr? LoadAttr(int itemId)
    {
        if (_attrCache.TryGetValue(itemId, out var cached)) return cached;
        ItemAttr? attr = null;
        try
        {
            if (InfoNode(itemId) is { } info)
            {
                var isEquip = itemId / 1_000_000 == 1;
                attr = new ItemAttr
                {
                    IsEquip   = isEquip,
                    Category  = itemId / 10000,
                    ReqLevel  = I(info, "reqLevel"),
                    ReqStr    = I(info, "reqSTR"),
                    ReqDex    = I(info, "reqDEX"),
                    ReqInt    = I(info, "reqINT"),
                    ReqLuk    = I(info, "reqLUK"),
                    ReqFame   = I(info, "reqPOP"),
                    ReqJob    = I(info, "reqJob"),
                    IncStr    = I(info, "incSTR"),  IncDex = I(info, "incDEX"),
                    IncInt    = I(info, "incINT"),  IncLuk = I(info, "incLUK"),
                    IncPad    = I(info, "incPAD"),  IncMad = I(info, "incMAD"),
                    IncPdd    = I(info, "incPDD"),  IncMdd = I(info, "incMDD"),
                    IncMhp    = I(info, "incMHP"),  IncMmp = I(info, "incMMP"),
                    IncAcc    = I(info, "incACC"),  IncEva = I(info, "incEVA"),
                    IncSpeed  = I(info, "incSpeed"), IncJump = I(info, "incJump"),
                    AttackSpeed = I(info, "attackSpeed"),
                    Upgrades  = I(info, "tuc"),
                    Price     = I(info, "price"),
                    Cash      = I(info, "cash") != 0,
                    Only      = I(info, "only") != 0,
                };
            }
        }
        catch { attr = null; }
        _attrCache[itemId] = attr;
        return attr;
    }

    private WzProperty? InfoNode(int itemId)
    {
        if (itemId / 1_000_000 == 1)
        {
            var category = Category(itemId);
            if (category is null || _characterWz is null) return null;
            return _characterWz.GetItem($"{category}/{itemId:D8}.img/info") as WzProperty;
        }
        if (_itemWz is null) return null;
        var folder = (itemId / 1_000_000) switch
        {
            2 => "Consume", 3 => "Install", 4 => "Etc", 5 => "Cash", _ => (string?)null,
        };
        if (folder is null) return null;
        return _itemWz.GetItem($"{folder}/{itemId / 10000:D4}.img/{itemId:D8}/info") as WzProperty;
    }

    private static int I(WzProperty p, string key) => p.Get(key) switch
    {
        int i => i, short s => s, long l => (int)l, byte b => b, _ => 0,
    };

    // Equip (1xxxxxx): Character.wz/<Category>/<id:D8>.img/info/icon
    private WzSprite? LoadEquipIcon(int itemId)
    {
        var category = Category(itemId);
        if (category is null || _characterWz is null) return null;
        return Resolve(_characterWz, $"{category}/{itemId:D8}.img/info");
    }

    // Consume/Install/Etc/Cash (2..5xxxxxx): Item.wz/<Cat>/<id/10000:D4>.img/<id:D8>/info/icon
    private WzSprite? LoadItemIcon(int itemId)
    {
        if (_itemWz is null) return null;
        var folder = (itemId / 1_000_000) switch
        {
            2 => "Consume",
            3 => "Install",
            4 => "Etc",
            5 => "Cash",
            _ => null,
        };
        if (folder is null) return null;
        var img = itemId / 10000;   // 02000000 → 200 → "0200"
        return Resolve(_itemWz, $"{folder}/{img:D4}.img/{itemId:D8}/info");
    }

    // icon (preferred) → iconRaw (fallback), resolving a UOL and uploading the canvas.
    private WzSprite? Resolve(WzPackage wz, string infoPath)
    {
        var node = wz.GetItem($"{infoPath}/icon") ?? wz.GetItem($"{infoPath}/iconRaw");
        if (node is WzUol uol) node = uol.Resolve();
        return node is WzCanvas canvas ? _loader.Load(canvas) : null;
    }

    // Character.wz folder for an equip id, by the 4-digit category (itemId / 10000).
    private static string? Category(int itemId)
    {
        var cat = itemId / 10000;
        return cat switch
        {
            100 => "Cap",
            101 or 102 or 103 => "Accessory",          // face / eye / earring
            104 => "Coat",
            105 => "Longcoat",                          // overall
            106 => "Pants",
            107 => "Shoes",
            108 => "Glove",
            109 => "Shield",
            110 => "Cape",
            111 => "Ring",
            >= 112 and <= 119 => "Accessory",          // pendant / belt / medal / shoulder / badge / etc.
            >= 130 and <= 179 => "Weapon",
            >= 190 and <= 193 => "TamingMob",          // mounts / saddle
            _ => null,
        };
    }
}

/// <summary>Parsed item attributes for the tooltip (a subset of the WZ <c>info</c> node):
/// requirements + stat bonuses. Stat values are the item's base WZ values.</summary>
public sealed class ItemAttr
{
    public bool IsEquip;
    public int  Category;     // itemId / 10000
    public int  ReqLevel, ReqStr, ReqDex, ReqInt, ReqLuk, ReqFame, ReqJob;
    public int  IncStr, IncDex, IncInt, IncLuk;
    public int  IncPad, IncMad, IncPdd, IncMdd, IncMhp, IncMmp, IncAcc, IncEva, IncSpeed, IncJump;
    public int  AttackSpeed, Upgrades, Price;
    public bool Cash, Only;
}

