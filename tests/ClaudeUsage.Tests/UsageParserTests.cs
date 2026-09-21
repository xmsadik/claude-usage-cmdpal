using System.Globalization;
using ClaudeUsage.Core;
using Xunit;

namespace ClaudeUsage.Tests;

public class UsageParserTests
{
    [Fact]
    public void PercentPayload_ScalesByHundred()
    {
        const string json = """
            { "five_hour": { "utilization": 37.0, "resets_at": "2026-09-21T18:00:00Z" } }
            """;

        var limits = UsageParser.Parse(json);

        var session = Assert.Single(limits);
        Assert.Equal("Session (5-hour)", session.Label);
        Assert.Equal(0.37, session.Fraction, precision: 6);
    }

    [Fact]
    public void FractionPayload_PassesThroughUnscaled()
    {
        const string json = """
            { "five_hour": { "utilization": 0.37, "resets_at": "2026-09-21T18:00:00Z" } }
            """;

        var limits = UsageParser.Parse(json);

        var session = Assert.Single(limits);
        Assert.Equal(0.37, session.Fraction, precision: 6);
    }

    [Fact]
    public void MixedPayload_OneValueAtLeastOneForcesPercentScaleForAll()
    {
        // 1.0 alongside a fraction-looking 0.37 forces the whole payload to percent scale,
        // so 1.0 becomes 1%, not 100%.
        const string json = """
            {
              "five_hour": { "utilization": 1.0, "resets_at": "2026-09-21T18:00:00Z" },
              "seven_day": { "utilization": 0.37, "resets_at": "2026-09-25T00:00:00Z" }
            }
            """;

        var limits = UsageParser.Parse(json);

        var session = limits.Single(l => l.Label == "Session (5-hour)");
        var weekly = limits.Single(l => l.Label == "Weekly (7-day)");
        Assert.Equal(0.01, session.Fraction, precision: 6);
        Assert.Equal(0.0037, weekly.Fraction, precision: 6);
    }

    [Fact]
    public void StringPercentValue_IsParsed()
    {
        const string json = """
            { "five_hour": { "utilization": "37%", "resets_at": "2026-09-21T18:00:00Z" } }
            """;

        var limits = UsageParser.Parse(json);

        var session = Assert.Single(limits);
        Assert.Equal(0.37, session.Fraction, precision: 6);
    }

    [Fact]
    public void ScopedLimits_AreIncludedAndDeduped()
    {
        const string json = """
            {
              "five_hour": { "utilization": 10.0, "resets_at": "2026-09-21T18:00:00Z" },
              "limits": [
                {
                  "kind": "weekly",
                  "percent": 22.5,
                  "resets_at": "2026-09-25T00:00:00Z",
                  "scope": { "model": { "display_name": "Fable", "id": "fable-1" } }
                },
                {
                  "kind": "weekly",
                  "percent": 99.0,
                  "resets_at": "2026-09-25T00:00:00Z",
                  "scope": { "model": { "display_name": "Fable", "id": "fable-1" } }
                }
              ]
            }
            """;

        var limits = UsageParser.Parse(json);

        var fable = limits.Where(l => l.Label.StartsWith("Fable", StringComparison.Ordinal)).ToList();
        var fableLimit = Assert.Single(fable);
        Assert.Equal("Fable Weekly", fableLimit.Label);
        Assert.Equal(0.225, fableLimit.Fraction, precision: 6); // first entry wins the dedupe
    }

    [Fact]
    public void SevenDayOauthApps_PreferredOverSevenDay()
    {
        const string json = """
            {
              "seven_day": { "utilization": 90.0, "resets_at": "2026-09-25T00:00:00Z" },
              "seven_day_oauth_apps": { "utilization": 10.0, "resets_at": "2026-09-26T00:00:00Z" }
            }
            """;

        var limits = UsageParser.Parse(json);

        var weekly = Assert.Single(limits);
        Assert.Equal(0.10, weekly.Fraction, precision: 6);
    }

    [Fact]
    public void EpochMillisecondReset_IsParsed()
    {
        var resetMs = DateTimeOffset.Parse("2026-09-21T18:00:00Z", CultureInfo.InvariantCulture).ToUnixTimeMilliseconds();
        var json = $$"""
            { "five_hour": { "utilization": 37.0, "resets_at": "{{resetMs}}" } }
            """;

        var limits = UsageParser.Parse(json);

        var session = Assert.Single(limits);
        Assert.Equal(DateTimeOffset.Parse("2026-09-21T18:00:00Z", CultureInfo.InvariantCulture), session.ResetsAt);
    }

    [Fact]
    public void IsoReset_IsParsed()
    {
        const string json = """
            { "five_hour": { "utilization": 37.0, "resets_at": "2026-09-21T18:00:00Z" } }
            """;

        var limits = UsageParser.Parse(json);

        var session = Assert.Single(limits);
        Assert.Equal(DateTimeOffset.Parse("2026-09-21T18:00:00Z", CultureInfo.InvariantCulture), session.ResetsAt);
    }

    [Fact]
    public void MissingBuckets_ReturnsEmpty()
    {
        const string json = "{}";

        var limits = UsageParser.Parse(json);

        Assert.Empty(limits);
    }
}
