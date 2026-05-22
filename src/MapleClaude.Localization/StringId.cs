namespace MapleClaude.Localization;

/// <summary>
/// Named <see cref="StringPool"/> ids for strings the client looks up by id.
/// Values are the v95 client's StringPool indices, verified against the bundled
/// English language pack. Centralising them here keeps call sites free of magic
/// numbers and makes the id→use-site mapping reviewable in one place.
/// </summary>
public static class StringId
{
    // ── Drop pick-up messages (CWvsContext::OnDropPickUpMessage) ──────────────
    public const int DropCannotGetAnymoreItems = 308;   // "You can't get anymore items."
    public const int DropCannotAcquireItems    = 5337;  // "You cannot acquire any items."

    // ── Job names: server jobId → StringPool id ───────────────────────────────
    // Explorer jobs map to the pack's job-name block (12–64); Magician's base
    // name and the Cygnus/Aran base names live further along. jobIds absent here
    // fall back to the built-in name table (no regression).
    public static readonly IReadOnlyDictionary<int, int> JobNameId = new Dictionary<int, int>
    {
        [0]    = 12,                                              // Beginner
        [100]  = 22, [110] = 23, [111] = 24, [112] = 25,         // Warrior line
        [120]  = 26, [121] = 27, [122] = 28,
        [130]  = 29, [131] = 30, [132] = 31,
        [200]  = 6735, [210] = 35, [211] = 36, [212] = 37,       // Magician line
        [220]  = 38, [221] = 39, [222] = 40,
        [230]  = 41, [231] = 42, [232] = 43,
        [300]  = 14, [310] = 45, [311] = 46, [312] = 47,         // Bowman line
        [320]  = 48, [321] = 49, [322] = 50,
        [400]  = 15, [410] = 52, [411] = 53, [412] = 54,         // Thief line
        [420]  = 55, [421] = 56, [422] = 57,
        [500]  = 16, [510] = 58, [511] = 59, [512] = 60,         // Pirate line
        [520]  = 61, [521] = 62, [522] = 63,
        [1000] = 64,                                             // Noblesse
        [1100] = 6707, [1200] = 6697, [1300] = 6794,             // Cygnus bases
        [1400] = 6739, [1500] = 6767,
        [2000] = 6692, [2100] = 6692,                            // Aran
    };
}
