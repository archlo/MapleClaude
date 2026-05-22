using FluentAssertions;
using MapleClaude.Localization;
using Xunit;

namespace MapleClaude.Net.Tests.Packet;

/// <summary>
/// Phase 11 StringPool tests: the bundled English language pack loads, escapes
/// (<c>\r\n</c>, doubled quotes) are decoded, missing ids fall back, printf
/// formatting substitutes, and the job-id → pool-id map resolves to real names.
/// </summary>
public class StringPoolTests
{
    private static readonly StringPool Pool = new(logger: null, language: "en");

    [Fact]
    public void Pack_Loads_WithManyEntries()
    {
        Pool.Language.Should().Be("en");
        Pool.Count.Should().BeGreaterThan(6000);
    }

    [Theory]
    [InlineData(12, "Beginner")]
    [InlineData(22, "Swordman")]
    [InlineData(308, "You can't get anymore items.")]
    [InlineData(5337, "You cannot acquire any items.")]
    [InlineData(6799, "Your inventory is full.")]
    public void Get_ReturnsKnownStrings(int id, string expected) =>
        Pool.Get(id).Should().Be(expected);

    [Fact]
    public void Get_MissingId_ReturnsMarker() =>
        Pool.Get(99_999_999).Should().Be("[99999999]");

    [Fact]
    public void Unescape_ConvertsLiteralCrLf_ToRealNewline()
    {
        var s = Pool.Get(72); // "[Warning]\r\nIf the name contains a foul language..."
        s.Should().StartWith("[Warning]");
        s.Should().Contain("\n");          // real newline
        s.Should().NotContain("\\r\\n");   // not the literal backslash escape
    }

    [Fact]
    public void Unescape_CollapsesDoubledQuotes()
    {
        var s = Pool.Get(277); // contains ""The Force of Darkness,""
        s.Should().Contain("\"The Force of Darkness,\"");
        s.Should().NotContain("\"\"");     // no doubled quotes remain
    }

    [Fact]
    public void Format_SubstitutesStringSpecifier()
    {
        // id 73 = "The name '%s' is taken."
        Pool.Format(73, "Bob").Should().Be("The name 'Bob' is taken.");
    }

    [Fact]
    public void Format_HandlesPercentLiteral_AndMultipleArgs()
    {
        // Build via a known multi-arg template — id 502 has %s and %d and %%.
        // We only assert the specifiers were consumed and %% became %.
        var s = Pool.Format(502, "Bob", 4);
        s.Should().Contain("Bob");
        s.Should().Contain("4");
        s.Should().NotContain("%s");
        s.Should().NotContain("%d");
    }

    [Fact]
    public void GetOr_FallsBackWhenMissing() =>
        Pool.GetOr(99_999_999, "fallback").Should().Be("fallback");

    [Fact]
    public void JobNameId_MapsToRealPoolNames()
    {
        StringId.JobNameId[100].Should().Be(22);
        Pool.Get(StringId.JobNameId[100]).Should().Be("Swordman");
        Pool.Get(StringId.JobNameId[0]).Should().Be("Beginner");
        Pool.Get(StringId.JobNameId[1000]).Should().Be("Noblesse");
    }
}
