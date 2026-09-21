using System.Globalization;
using ClaudeUsage.Core;
using Xunit;

namespace ClaudeUsage.Tests;

public class FormatTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-21T12:00:00Z", CultureInfo.InvariantCulture);

    [Fact]
    public void Countdown_20h59m_DoesNotInflateToOneDay()
    {
        var resetsAt = Now.AddHours(20).AddMinutes(59);

        var text = Format.Countdown(resetsAt, Now);

        Assert.Equal("resets in 20h 59m", text);
    }

    [Fact]
    public void Countdown_49h_ReportsTwoDaysOneHour()
    {
        var resetsAt = Now.AddHours(49);

        var text = Format.Countdown(resetsAt, Now);

        Assert.Equal("resets in 2d 1h", text);
    }

    [Fact]
    public void Countdown_PastTimestamp_ReturnsResetsNow()
    {
        var resetsAt = Now.AddMinutes(-5);

        var text = Format.Countdown(resetsAt, Now);

        Assert.Equal("resets now", text);
    }

    [Fact]
    public void Countdown_UnderOneHour_OmitsHours()
    {
        var resetsAt = Now.AddMinutes(14);

        var text = Format.Countdown(resetsAt, Now);

        Assert.Equal("resets in 14m", text);
    }

    [Theory]
    [InlineData(999, "999")]
    [InlineData(1000, "1.0K")]
    [InlineData(1_200, "1.2K")]
    [InlineData(998_000, "998.0K")]
    [InlineData(1_000_000, "1.0M")]
    [InlineData(3_400_000, "3.4M")]
    [InlineData(1_000_000_000, "1.0B")]
    [InlineData(1_100_000_000, "1.1B")]
    public void Tokens_FormatsAtUnitBoundaries(double n, string expected)
    {
        Assert.Equal(expected, Format.Tokens(n));
    }

    [Fact]
    public void Tokens_UsesInvariantDecimalPointUnderTurkishCulture()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");

            // tr-TR formats 37.0 as "37,0" if this were culture-sensitive; Format.Tokens
            // must always use '.' regardless of the machine's locale.
            Assert.Equal("1.2M", Format.Tokens(1_200_000));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
