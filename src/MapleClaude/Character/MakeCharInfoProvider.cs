using MapleClaude.Wz;
using Microsoft.Extensions.Logging;

namespace MapleClaude.Character;

/// <summary>
/// Loads the character-creation appearance option lists from
/// <c>Etc.wz/MakeCharInfo.img</c>. Each option category maps 1:1 to a slot in the
/// <c>CreateNewCharacter</c> packet:
/// <list type="bullet">
///   <item>0 face, 1 hairBase, 2 hairColor (0/7/3/2 added to hairBase), 3 skin (0–3),</item>
///   <item>4 coat, 5 pants, 6 shoes, 7 weapon.</item>
/// </list>
/// The standard set is <c>Info/Char{Male,Female}</c>; "legend" classes have their own
/// top-level sets (<c>ResistanceChar*</c>, <c>EvanChar*</c>, <c>OrientChar*</c> for Aran,
/// <c>PremiumChar*</c>). Kinoko's <c>isValidStartingItem</c> validates against the union of
/// all sets, so any id returned here is accepted by the server.
/// </summary>
public sealed class MakeCharInfoProvider
{
    public const int CatFace = 0;
    public const int CatHair = 1;
    public const int CatHairColor = 2;
    public const int CatSkin = 3;
    public const int CatCoat = 4;
    public const int CatPants = 5;
    public const int CatShoes = 6;
    public const int CatWeapon = 7;
    public const int CatCount = 8;

    // section|gender|cat -> id list
    private readonly Dictionary<string, int[]> _options = new();
    // section|gender|type|id -> display name (MakeCharInfo.img/Name/{prefix}Char{Male,Female}/...)
    private readonly Dictionary<string, string> _names = new();

    public MakeCharInfoProvider(WzPackage? etcWz, ILogger logger)
    {
        try { Load(etcWz); }
        catch (Exception ex) { logger.LogWarning(ex, "MakeCharInfo load failed"); }
    }

    /// <summary>Option ids for a category, for the given race section + gender. Falls back
    /// to the standard "Info" set when a section omits a category.</summary>
    public int[] Options(string section, bool male, int cat)
    {
        if (_options.TryGetValue(Key(section, male, cat), out var v) && v.Length > 0) return v;
        return _options.TryGetValue(Key("Info", male, cat), out var fb) ? fb : Array.Empty<int>();
    }

    public bool HasData => _options.Count > 0;

    /// <summary>UI race id (RaceSelectStage button order) → MakeCharInfo section.
    /// Dual=0, Explorer=1, Cygnus=2, Aran=3, Evan=4, Resistance=5.</summary>
    public static string SectionForRace(int uiRace) => uiRace switch
    {
        5 => "Resistance",
        4 => "Evan",
        3 => "Orient",      // Aran
        _ => "Info",        // Explorer, Cygnus, Dual Blade
    };

    private static string Key(string section, bool male, int cat) => $"{section}|{(male ? 'M' : 'F')}|{cat}";

    private void Load(WzPackage? etc)
    {
        // A ".img" node resolves to a WzImage — read options from its root property.
        if (etc?.GetItem("MakeCharInfo.img") is not WzImage img) return;
        var root = img.Root;
        foreach (var section in new[] { "Info", "Resistance", "Evan", "Orient", "Premium" })
        {
            LoadSection(root, section, male: true);
            LoadSection(root, section, male: false);
            LoadNames(root, section, male: true);
            LoadNames(root, section, male: false);
        }
    }

    private void LoadSection(WzProperty root, string section, bool male)
    {
        var gender = male ? "CharMale" : "CharFemale";
        // "Info" lives under Info/Char{Male,Female}; the legend sets are top-level "<Section>Char*".
        var node = section == "Info"
            ? (root.Get("Info") as WzProperty)?.Get(gender) as WzProperty
            : root.Get($"{section}{gender}") as WzProperty;
        if (node is null) return;

        for (var cat = 0; cat < CatCount; cat++)
        {
            if (node.Get(cat.ToString(System.Globalization.CultureInfo.InvariantCulture)) is not WzProperty catNode)
            {
                continue;
            }
            var ids = new List<int>();
            var i = 0;
            while (catNode.Get(i.ToString(System.Globalization.CultureInfo.InvariantCulture)) is { } raw)
            {
                switch (raw)
                {
                    case int id: ids.Add(id); break;
                    case short s: ids.Add(s); break;
                }
                i++;
            }
            if (ids.Count > 0) _options[Key(section, male, cat)] = ids.ToArray();
        }
    }

    /// <summary>Display name for a Name-table entry — type 1 hairStyle (full hair id),
    /// 2 hairColor (color code), 3 skin (skin code); e.g. "Buzz Hair"/"Black"/"Light".
    /// Falls back to the standard "Info" set. Face/equips have no Name entry (use String.wz).</summary>
    public string? Name(string section, bool male, int type, int id)
        => _names.TryGetValue(NameKey(section, male, type, id), out var v) ? v
         : _names.TryGetValue(NameKey("Info", male, type, id), out var fb) ? fb : null;

    private static string NameKey(string section, bool male, int type, int id)
        => $"{section}|{(male ? 'M' : 'F')}|{type}|{id}";

    // MakeCharInfo.img/Name/{prefix}Char{Male,Female}/{type}/{id} = display string.
    // prefix: Info -> "" (CharMale); legend sets -> "Evan"/"Resistance"/"Orient"/"Premium".
    private void LoadNames(WzProperty root, string section, bool male)
    {
        if (root.Get("Name") is not WzProperty nameRoot) return;
        var prefix = section == "Info" ? "" : section;
        var gender = male ? "CharMale" : "CharFemale";
        if (nameRoot.Get($"{prefix}{gender}") is not WzProperty node) return;
        foreach (var (typeKey, typeVal) in node.Items)
        {
            if (typeVal is not WzProperty typeNode || !int.TryParse(typeKey, out var type)) continue;
            foreach (var (idKey, idVal) in typeNode.Items)
                if (idVal is string s && int.TryParse(idKey, out var id))
                    _names[NameKey(section, male, type, id)] = s;
        }
    }
}
