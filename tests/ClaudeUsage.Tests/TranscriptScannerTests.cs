using System.Globalization;
using ClaudeUsage.Core;
using Xunit;

namespace ClaudeUsage.Tests;

/// <summary>Scans synthetic fixture transcripts under a temp dir — never the real ~/.claude/projects.</summary>
public sealed class TranscriptScannerTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 9, 21, 12, 0, 0, DateTimeKind.Local);

    private readonly string _claudeDir;
    private readonly string _projectDir;

    public TranscriptScannerTests()
    {
        _claudeDir = Path.Combine(Path.GetTempPath(), "ClaudeUsageTests_" + Guid.NewGuid().ToString("N"));
        _projectDir = Path.Combine(_claudeDir, "projects", "demo-project");
        Directory.CreateDirectory(_projectDir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_claudeDir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void DuplicateMessageId_AcrossTwoFiles_CountedOnce()
    {
        var line = AssistantLine(id: "msg-shared", model: "claude-x", input: 100, output: 50, sessionId: "s1");
        File.WriteAllText(Path.Combine(_projectDir, "a.jsonl"), line);
        File.WriteAllText(Path.Combine(_projectDir, "b.jsonl"), line); // Same message.id, different file.

        var stats = TranscriptScanner.Scan(_claudeDir, Now);

        Assert.Equal(1, stats.TotalPrompts);
        Assert.Equal(150, stats.TodayTotalTokens);
    }

    [Fact]
    public void CamelCaseUsageFields_AreRead()
    {
        var line = """
            {"type":"assistant","sessionId":"s1","timestamp":"2026-09-21T10:00:00Z","message":{"id":"msg-camel","role":"assistant","model":"claude-x","usage":{"inputTokens":40,"outputTokens":10,"cacheReadInputTokens":5,"cacheCreationInputTokens":3}}}
            """;
        File.WriteAllText(Path.Combine(_projectDir, "camel.jsonl"), line);

        var stats = TranscriptScanner.Scan(_claudeDir, Now);

        Assert.Equal(1, stats.TotalPrompts);
        Assert.Equal(58, stats.TodayTotalTokens);
        var usage = Assert.Single(stats.ModelUsage);
        Assert.Equal("claude-x", usage.Key);
        Assert.Equal(40, usage.Value.InputTokens);
        Assert.Equal(10, usage.Value.OutputTokens);
        Assert.Equal(5, usage.Value.CacheReadInputTokens);
        Assert.Equal(3, usage.Value.CacheCreationInputTokens);
    }

    [Fact]
    public void NonAssistantLines_AreIgnored()
    {
        var userLine = """
            {"type":"user","sessionId":"s1","timestamp":"2026-09-21T10:00:00Z","message":{"role":"user","usage":{"input_tokens":999,"output_tokens":999}}}
            """;
        File.WriteAllText(Path.Combine(_projectDir, "user.jsonl"), userLine);

        var stats = TranscriptScanner.Scan(_claudeDir, Now);

        Assert.Equal(0, stats.TotalPrompts);
        Assert.Equal(0, stats.TodayTotalTokens);
        Assert.Empty(stats.ModelUsage);
    }

    [Fact]
    public void GarbledLine_IsSkippedWithoutAbortingTheFile()
    {
        var goodLine = AssistantLine(id: "msg-good", model: "claude-x", input: 10, output: 5, sessionId: "s1");
        var content = string.Join(
            '\n',
            goodLine,
            """{ this is not valid json but mentions "usage": anyway """,
            goodLine.Replace("msg-good", "msg-good-2", StringComparison.Ordinal));
        File.WriteAllText(Path.Combine(_projectDir, "mixed.jsonl"), content);

        var stats = TranscriptScanner.Scan(_claudeDir, Now);

        // Both valid lines counted, garbled line skipped without throwing.
        Assert.Equal(2, stats.TotalPrompts);
        Assert.Equal(30, stats.TodayTotalTokens);
    }

    [Fact]
    public void NoProjectsDirectory_ReturnsEmptyStatsWithSevenDayBuckets()
    {
        Directory.Delete(_claudeDir, recursive: true);

        var stats = TranscriptScanner.Scan(_claudeDir, Now);

        Assert.Equal(0, stats.TotalPrompts);
        Assert.Equal(7, stats.RecentDays.Count);
        Assert.All(stats.RecentDays, d => Assert.Equal(0, d.Tokens));
    }

    private static string AssistantLine(string id, string model, long input, long output, string sessionId)
    {
        const string template = """
            {"type":"assistant","sessionId":"__SESSION__","timestamp":"2026-09-21T10:00:00Z","message":{"id":"__ID__","role":"assistant","model":"__MODEL__","usage":{"input_tokens":__IN__,"output_tokens":__OUT__}}}
            """;
        return template
            .Replace("__SESSION__", sessionId, StringComparison.Ordinal)
            .Replace("__ID__", id, StringComparison.Ordinal)
            .Replace("__MODEL__", model, StringComparison.Ordinal)
            .Replace("__IN__", input.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("__OUT__", output.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }
}
