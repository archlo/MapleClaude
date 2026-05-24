using MapleClaude.Wz;
using Microsoft.Extensions.Logging;

namespace MapleClaude.Character;

/// <summary>
/// Client-side forbidden-word filter for character names, mirroring the v95 client's
/// <c>is_valid_character_name</c> word check (called from <c>CLogin::SendCheckDuplicateIDPacket</c>
/// before the CheckDuplicatedID packet). The list is the union of <c>Etc.wz/ForbiddenName.img</c>
/// and <c>Etc.wz/Curse.img</c> — each a flat list of string children ("0","1",…). Each raw entry
/// is split on ',', trimmed and lowercased; a name is forbidden if (lowercased) it CONTAINS any
/// entry as a substring.
/// <para>The Kinoko server does a narrower exact match against ForbiddenName.img only
/// (<c>EtcProvider.isForbiddenName</c>), so this client filter is intentionally the stricter,
/// authentic one — it never lets through a name the server would reject.</para>
/// </summary>
public sealed class ForbiddenNameProvider
{
    private readonly List<string> _words = new();

    public ForbiddenNameProvider(WzPackage? etcWz, ILogger logger)
    {
        try
        {
            Load(etcWz, "ForbiddenName.img");
            Load(etcWz, "Curse.img");
        }
        catch (Exception ex) { logger.LogWarning(ex, "ForbiddenName/Curse load failed"); }
        logger.LogInformation("ForbiddenName: {Count} words loaded", _words.Count);
    }

    public bool HasData => _words.Count > 0;

    /// <summary>True if the (lowercased) name contains any forbidden word as a substring.</summary>
    public bool IsForbidden(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        var lower = name.ToLowerInvariant();
        foreach (var w in _words)
            if (lower.Contains(w, StringComparison.Ordinal)) return true;
        return false;
    }

    private void Load(WzPackage? etc, string imgName)
    {
        // A ".img" node resolves to a WzImage; its property children are the string entries.
        if (etc?.GetItem(imgName) is not WzImage img) return;
        foreach (var (_, val) in img.Root.Items)
            if (val is string raw) AddEntry(raw);
    }

    private void AddEntry(string raw)
    {
        // Some entries are comma-delimited; the client splits on ',', trims, lowercases.
        foreach (var part in raw.Split(','))
        {
            var w = part.Trim().ToLowerInvariant();
            if (w.Length > 0) _words.Add(w);
        }
    }
}
