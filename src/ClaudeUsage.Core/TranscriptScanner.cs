using System.Globalization;
using System.Text.Json;

namespace ClaudeUsage.Core;

public sealed record ModelUsage(long InputTokens, long OutputTokens, long CacheReadInputTokens, long CacheCreationInputTokens)
{
    public long Total => InputTokens + OutputTokens + CacheReadInputTokens + CacheCreationInputTokens;
}

public sealed record DayTokens(string Date, long Tokens);

/// <summary>Local-machine-only token/prompt stats, read from ~/.claude/projects/**/*.jsonl.</summary>
public sealed record LocalStats(
    int TodayPrompts,
    int TodaySessions,
    long TodayTotalTokens,
    IReadOnlyList<DayTokens> RecentDays,
    IReadOnlyDictionary<string, ModelUsage> ModelUsage,
    int TotalPrompts,
    int TotalSessions,
    int ActiveDays);

/// <summary>
/// Scans Claude Code transcripts for local token/prompt stats. Ports the ps1 reference's
/// Get-TranscriptStats: no incremental cache (message.id dedup happens across files, so a
/// per-file offset would be unreliable), so this is a full scan every time, intended to run
/// off the UI thread and be memoized by the caller.
/// </summary>
public static class TranscriptScanner
{
    public static LocalStats Scan(string claudeDir) => Scan(claudeDir, DateTime.Now);

    /// <summary>Testable overload that takes "now" explicitly.</summary>
    public static LocalStats Scan(string claudeDir, DateTime now)
    {
        var today = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var recentDates = new string[7];
        for (var i = 0; i < 7; i++)
        {
            recentDates[i] = now.AddDays(i - 6).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        var recentTotals = new Dictionary<string, long>();
        foreach (var d in recentDates)
        {
            recentTotals[d] = 0;
        }

        var projectsDir = Path.Combine(claudeDir, "projects");
        if (!Directory.Exists(projectsDir))
        {
            return new LocalStats(
                0, 0, 0,
                [.. recentDates.Select(d => new DayTokens(d, 0))],
                new Dictionary<string, ModelUsage>(),
                0, 0, 0);
        }

        var seenMessageKeys = new HashSet<string>();
        var sessions = new HashSet<string>();
        var activeDays = new HashSet<string>();
        var todaySessions = new HashSet<string>();
        var models = new Dictionary<string, long[]>(); // [input, output, cacheRead, cacheWrite]

        var prompts = 0;
        var todayPrompts = 0;
        long todayTotal = 0;

        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(projectsDir, "*.jsonl", SearchOption.AllDirectories);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            files = [];
        }

        foreach (var file in files)
        {
            ScanFile(
                file, today, recentTotals, seenMessageKeys, sessions, activeDays, todaySessions, models,
                ref prompts, ref todayPrompts, ref todayTotal);
        }

        var modelUsage = models.ToDictionary(
            kv => kv.Key,
            kv => new ModelUsage(kv.Value[0], kv.Value[1], kv.Value[2], kv.Value[3]));

        return new LocalStats(
            todayPrompts,
            todaySessions.Count,
            todayTotal,
            [.. recentDates.Select(d => new DayTokens(d, recentTotals[d]))],
            modelUsage,
            prompts,
            sessions.Count,
            activeDays.Count);
    }

    private static void ScanFile(
        string file,
        string today,
        Dictionary<string, long> recentTotals,
        HashSet<string> seenMessageKeys,
        HashSet<string> sessions,
        HashSet<string> activeDays,
        HashSet<string> todaySessions,
        Dictionary<string, long[]> models,
        ref int prompts,
        ref int todayPrompts,
        ref long todayTotal)
    {
        StreamReader reader;
        try
        {
            var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            reader = new StreamReader(stream);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // File locked/being written/deleted mid-scan: skip it entirely.
            return;
        }

        using (reader)
        {
            var lineNumber = 0;
            while (true)
            {
                string? line;
                try
                {
                    line = reader.ReadLine();
                }
                catch (IOException)
                {
                    break;
                }

                if (line is null)
                {
                    break;
                }

                lineNumber++;

                // Cheap pre-filter before parsing keeps files full of unrelated lines cheap.
                if (line.IndexOf("\"usage\":", StringComparison.Ordinal) < 0)
                {
                    continue;
                }

                ProcessLine(
                    line, file, lineNumber, today, recentTotals, seenMessageKeys, sessions, activeDays,
                    todaySessions, models, ref prompts, ref todayPrompts, ref todayTotal);
            }
        }
    }

    private static void ProcessLine(
        string line,
        string file,
        int lineNumber,
        string today,
        Dictionary<string, long> recentTotals,
        HashSet<string> seenMessageKeys,
        HashSet<string> sessions,
        HashSet<string> activeDays,
        HashSet<string> todaySessions,
        Dictionary<string, long[]> models,
        ref int prompts,
        ref int todayPrompts,
        ref long todayTotal)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(line);
        }
        catch (JsonException)
        {
            return; // Garbled line: skip it, keep scanning the rest of the file.
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            var message = JsonHelpers.TryGet(root, "message");
            var type = JsonHelpers.GetStringOrEmpty(root, "type");
            var role = message is JsonElement m ? JsonHelpers.GetStringOrEmpty(m, "role") : string.Empty;
            if (type != "assistant" && role != "assistant")
            {
                return;
            }

            var usage = message is JsonElement msg ? JsonHelpers.TryGet(msg, "usage") : null;
            usage ??= JsonHelpers.TryGet(root, "usage");
            if (usage is not JsonElement usageEl)
            {
                return;
            }

            var id = message is JsonElement m2 ? JsonHelpers.GetStringOrEmpty(m2, "id") : string.Empty;
            if (id.Length == 0)
            {
                id = JsonHelpers.GetStringOrEmpty(root, "messageId");
            }

            var key = id.Length > 0 ? id : $"{file}:{lineNumber}";
            if (!seenMessageKeys.Add(key))
            {
                return; // Same message counted from another file/line already.
            }

            var inTok = JsonHelpers.ToInt64Lenient(JsonHelpers.TryGet(usageEl, "input_tokens") ?? JsonHelpers.TryGet(usageEl, "inputTokens"));
            var outTok = JsonHelpers.ToInt64Lenient(JsonHelpers.TryGet(usageEl, "output_tokens") ?? JsonHelpers.TryGet(usageEl, "outputTokens"));
            var cacheRead = JsonHelpers.ToInt64Lenient(JsonHelpers.TryGet(usageEl, "cache_read_input_tokens") ?? JsonHelpers.TryGet(usageEl, "cacheReadInputTokens"));
            var cacheWrite = JsonHelpers.ToInt64Lenient(JsonHelpers.TryGet(usageEl, "cache_creation_input_tokens") ?? JsonHelpers.TryGet(usageEl, "cacheCreationInputTokens"));
            var total = inTok + outTok + cacheRead + cacheWrite;
            if (total <= 0)
            {
                return;
            }

            var model = message is JsonElement m3 ? JsonHelpers.GetStringOrEmpty(m3, "model") : string.Empty;
            if (model.Length == 0)
            {
                model = "claude";
            }

            var day = JsonHelpers.ParseTimestamp(JsonHelpers.RawString(JsonHelpers.TryGet(root, "timestamp")))
                ?.LocalDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            var sessionKey = JsonHelpers.GetStringOrEmpty(root, "sessionId");
            if (sessionKey.Length == 0)
            {
                sessionKey = file;
            }

            sessions.Add(sessionKey);
            if (day is not null)
            {
                activeDays.Add(day);
            }

            prompts++;

            if (!models.TryGetValue(model, out var totals))
            {
                totals = new long[4];
                models[model] = totals;
            }

            totals[0] += inTok;
            totals[1] += outTok;
            totals[2] += cacheRead;
            totals[3] += cacheWrite;

            if (day is not null && recentTotals.ContainsKey(day))
            {
                recentTotals[day] += total;
            }

            if (day == today)
            {
                todayPrompts++;
                todaySessions.Add(sessionKey);
                todayTotal += total;
            }
        }
    }
}
