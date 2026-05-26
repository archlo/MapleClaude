using FluentAssertions;
using MapleClaude.Domain;
using Xunit;

namespace MapleClaude.Domain.Tests;

public class JobConstantsTests
{
    // The full skill-book tab chain (beginner first, then each reached
    // advancement). This is the contract the Skill window relies on.
    [Theory]
    [InlineData(0, new[] { 0 })]                          // Explorer beginner
    [InlineData(100, new[] { 0, 100 })]                   // Warrior (1st)
    [InlineData(110, new[] { 0, 100, 110 })]              // Fighter (2nd)
    [InlineData(111, new[] { 0, 100, 110, 111 })]         // Crusader (3rd)
    [InlineData(112, new[] { 0, 100, 110, 111, 112 })]    // Hero (4th)
    [InlineData(120, new[] { 0, 100, 120 })]              // Page (2nd)
    [InlineData(121, new[] { 0, 100, 120, 121 })]         // White Knight (3rd)
    [InlineData(122, new[] { 0, 100, 120, 121, 122 })]    // Paladin (4th)
    [InlineData(130, new[] { 0, 100, 130 })]              // Spearman (2nd)
    [InlineData(131, new[] { 0, 100, 130, 131 })]         // Dragon Knight (3rd)
    [InlineData(132, new[] { 0, 100, 130, 131, 132 })]    // Dark Knight (4th)
    [InlineData(200, new[] { 0, 200 })]                   // Magician (1st)
    [InlineData(212, new[] { 0, 200, 210, 211, 212 })]    // Arch Mage F/P (4th)
    [InlineData(1000, new[] { 1000 })]                    // Noblesse (Cygnus beginner)
    [InlineData(2000, new[] { 2000 })]                    // Aran beginner
    [InlineData(3000, new[] { 3000 })]                    // Citizen (Resistance beginner)
    public void GetSkillRoots_returns_advancement_chain(int job, int[] expected) =>
        JobConstants.GetSkillRoots(job).Should().Equal(expected);

    [Theory]
    [InlineData(0, true)]
    [InlineData(1000, true)]
    [InlineData(2000, true)]
    [InlineData(2001, true)]   // Evan beginner — special-cased
    [InlineData(3000, true)]
    [InlineData(100, false)]
    [InlineData(112, false)]
    [InlineData(2200, false)]  // Evan 1st dragon is not a "beginner" job
    public void IsBeginner_matches_novice_jobs(int job, bool expected) =>
        JobConstants.IsBeginner(job).Should().Be(expected);

    [Theory]
    [InlineData(0, 0)]
    [InlineData(100, 1)]
    [InlineData(110, 2)]
    [InlineData(111, 3)]
    [InlineData(112, 4)]
    [InlineData(122, 4)]
    [InlineData(132, 4)]
    public void GetJobLevel_reads_advancement_tier(int job, int expected) =>
        JobConstants.GetJobLevel(job).Should().Be(expected);

    [Theory]
    [InlineData(112, 0)]       // Explorer Hero → Explorer beginner
    [InlineData(122, 0)]       // Explorer Paladin → Explorer beginner
    [InlineData(1112, 1000)]   // Cygnus → Noblesse
    [InlineData(2112, 2000)]   // Aran → Aran beginner
    [InlineData(3212, 3000)]   // Resistance → Citizen
    [InlineData(2001, 2001)]   // Evan beginner → itself
    [InlineData(2210, 2001)]   // Evan dragon job → Evan beginner
    public void GetBeginnerRoot_picks_race_novice(int job, int expected) =>
        JobConstants.GetBeginnerRoot(job).Should().Be(expected);
}
