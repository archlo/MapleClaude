namespace MapleClaude.Domain;

/// <summary>
/// Job-id → skill-root math for the Skill window's job-tier tabs. Mirrors the
/// upstream Kinoko <c>JobConstants.getSkillRootFromJob</c>, but returns the roots
/// in <em>client tab order</em> (beginner first, then 1st→4th job) — the order
/// the v95 client's <c>CUISkill</c> lays out its five tabs.
///
/// A job id reads as branch+advancement digits: the hundreds digit is the job
/// branch, the tens/ones the advancement within it — Warrior 100 → Fighter 110
/// → Crusader 111 → Hero 112; Page 120 → White Knight 121 → Paladin 122. The
/// thousands digit is the race (0 Explorer, 1 Cygnus, 2 Aran/Evan, 3 Resistance).
/// A learned skill's root is <c>skillId / 10000</c>, which equals one of these
/// job ids, so skills group onto tabs by matching that root.
/// </summary>
public static class JobConstants
{
    /// <summary>A beginner/novice job (Explorer 0, Noblesse 1000, Aran 2000, Evan 2001, Citizen 3000).</summary>
    public static bool IsBeginner(int job) => job % 1000 == 0 || job == 2001;

    /// <summary>Advancement tier: 0 beginner, 1 first, 2 second, 3 third, 4 fourth.</summary>
    public static int GetJobLevel(int job)
    {
        if (IsBeginner(job)) return 0;
        if (job % 100 == 0) return 1; // 100, 1100, 2100, 3200…
        if (job % 10 == 0) return 2;  // 110, 120, 130
        if (job % 10 == 1) return 3;  // 111, 121, 131
        return 4;                     // 112, 122, 132
    }

    /// <summary>The beginner/novice skill root for a job's race (Evan shares 2001).</summary>
    public static int GetBeginnerRoot(int job) =>
        job / 100 == 22 || job == 2001 ? 2001 : job / 1000 * 1000;

    /// <summary>
    /// Ordered skill roots for the Skill window tabs: beginner first, then each
    /// reached advancement (1st→4th). Tab <c>i</c> shows the skills under root
    /// <c>GetSkillRoots(job)[i]</c>. Examples: Hero 112 → [0, 100, 110, 111, 112];
    /// Paladin 122 → [0, 100, 120, 121, 122]; Beginner 0 → [0].
    /// </summary>
    public static int[] GetSkillRoots(int job)
    {
        var level = GetJobLevel(job);
        var roots = new List<int>(5) { GetBeginnerRoot(job) };
        if (level >= 1) roots.Add(job / 100 * 100);
        if (level >= 2) roots.Add(job / 10 * 10);
        if (level >= 3) roots.Add(level == 4 ? job - 1 : job);
        if (level >= 4) roots.Add(job);
        return roots.ToArray();
    }
}
