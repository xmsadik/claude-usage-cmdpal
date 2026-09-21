using System.Globalization;
using System.Text;

namespace ClaudeUsage.Core;

/// <summary>Builds the Markdown shown on the detail page's flyout.</summary>
public static class MarkdownReport
{
    private const int LimitBarWidth = 16;
    private const int DayBarWidth = 12;
    private const int ModelBarWidth = 12;

    public static string Build(UsageSnapshot snapshot, LocalStats? localStats, DateTimeOffset now, TimeSpan? refreshInterval = null)
    {
        var sb = new StringBuilder();

        sb.Append("# Claude Code");
        if (!string.IsNullOrEmpty(snapshot.Plan))
        {
            sb.Append(" · ").Append(snapshot.Plan);
        }

        sb.AppendLine();
        sb.AppendLine();

        if (snapshot.Status == UsageStatus.Ok)
        {
            var updated = snapshot.UpdatedAt.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture);
            sb.Append("_Updated ").Append(updated);
            if (refreshInterval is TimeSpan interval)
            {
                sb.Append(" · refreshes every ").Append(((int)interval.TotalMinutes).ToString(CultureInfo.InvariantCulture)).Append(" min");
            }

            if (snapshot.IsStale)
            {
                sb.Append(" · showing last known values");
            }

            sb.AppendLine("_");
        }
        else
        {
            sb.Append('_').Append(StatusLabel(snapshot.Status));
            if (!string.IsNullOrEmpty(snapshot.HelpText))
            {
                sb.Append(" — ").Append(snapshot.HelpText);
            }

            sb.AppendLine("_");
        }

        if (snapshot.Limits.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Limits");
            sb.AppendLine();
            sb.AppendLine("| Limit | Usage | | Resets |");
            sb.AppendLine("|---|---|---|---|");
            foreach (var limit in snapshot.Limits)
            {
                var bar = Format.Bar(limit.Fraction, LimitBarWidth);
                var percent = (limit.Fraction * 100).ToString("N1", CultureInfo.InvariantCulture);
                var resets = Format.Countdown(limit.ResetsAt, now);
                sb.AppendLine(CultureInfo.InvariantCulture, $"| {limit.Label} | `{bar}` | **{percent}%** | {resets} |");
            }
        }

        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("## Local stats · this machine only");
        sb.AppendLine();
        sb.AppendLine("_Claude Code transcripts on this PC. claude.ai and other devices are not included._");
        sb.AppendLine();

        if (localStats is null)
        {
            sb.AppendLine("_Scanning transcripts…_");
            return sb.ToString();
        }

        AppendTokensByDay(sb, localStats);
        AppendTokensByModel(sb, localStats);

        var todayPrompts = localStats.TodayPrompts.ToString(CultureInfo.InvariantCulture);
        var todaySessions = localStats.TodaySessions.ToString(CultureInfo.InvariantCulture);
        var totalPrompts = localStats.TotalPrompts.ToString(CultureInfo.InvariantCulture);
        var totalSessions = localStats.TotalSessions.ToString(CultureInfo.InvariantCulture);
        var activeDays = localStats.ActiveDays.ToString(CultureInfo.InvariantCulture);
        sb.AppendLine(
            CultureInfo.InvariantCulture,
            $"{todayPrompts} prompts · {todaySessions} sessions today — " +
            $"{totalPrompts} prompts · {totalSessions} sessions · " +
            $"{activeDays} active days overall");

        return sb.ToString();
    }

    private static void AppendTokensByDay(StringBuilder sb, LocalStats localStats)
    {
        sb.AppendLine("### Tokens by day");
        sb.AppendLine();

        if (localStats.RecentDays.Count == 0)
        {
            sb.AppendLine("_No local transcripts found._");
            sb.AppendLine();
            return;
        }

        var peak = localStats.RecentDays.Select(d => d.Tokens).DefaultIfEmpty(0).Max();
        if (peak <= 0)
        {
            peak = 1;
        }

        var today = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        sb.AppendLine("| Day | Usage | Tokens |");
        sb.AppendLine("|---|---|---|");
        foreach (var day in localStats.RecentDays)
        {
            var label = DateTime.TryParseExact(day.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
                ? parsed.ToString("ddd dd", CultureInfo.InvariantCulture)
                : day.Date;
            var bar = Format.Bar(day.Tokens / (double)peak, DayBarWidth);
            var tokens = Format.Tokens(day.Tokens);

            var row = $"| {label} | `{bar}` | {tokens} |";
            if (day.Date == today)
            {
                row = $"| **{label}** | `{bar}` | **{tokens}** |";
            }

            sb.AppendLine(row);
        }

        sb.AppendLine();
    }

    private static void AppendTokensByModel(StringBuilder sb, LocalStats localStats)
    {
        sb.AppendLine("### Tokens by model");
        sb.AppendLine();

        if (localStats.ModelUsage.Count == 0)
        {
            sb.AppendLine("_No local transcripts found._");
            sb.AppendLine();
            return;
        }

        var peak = localStats.ModelUsage.Values.Select(m => m.Total).DefaultIfEmpty(0).Max();
        if (peak <= 0)
        {
            peak = 1;
        }

        sb.AppendLine("| Model | Total | | In | Out | Cache write | Cache read |");
        sb.AppendLine("|---|---|---|---|---|---|---|");
        foreach (var (model, usage) in localStats.ModelUsage.OrderByDescending(kv => kv.Value.Total))
        {
            var bar = Format.Bar(usage.Total / (double)peak, ModelBarWidth);
            sb.AppendLine(
                CultureInfo.InvariantCulture,
                $"| {model} | `{bar}` | {Format.Tokens(usage.Total)} | {Format.Tokens(usage.InputTokens)} | " +
                $"{Format.Tokens(usage.OutputTokens)} | {Format.Tokens(usage.CacheCreationInputTokens)} | " +
                $"{Format.Tokens(usage.CacheReadInputTokens)} |");
        }

        sb.AppendLine();
    }

    private static string StatusLabel(UsageStatus status) => status switch
    {
        UsageStatus.AuthMissing => "Waiting for auth",
        UsageStatus.AuthExpired => "Limits unavailable",
        UsageStatus.RateLimited => "Limits unavailable",
        UsageStatus.Error => "Limits unavailable",
        UsageStatus.NoLimits => "No limits reported",
        _ => string.Empty,
    };
}
