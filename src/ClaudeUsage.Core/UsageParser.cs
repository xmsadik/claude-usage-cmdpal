using System.Globalization;
using System.Text.Json;

namespace ClaudeUsage.Core;

/// <summary>One row of the usage endpoint's response: a labeled limit with its fill fraction and reset time.</summary>
public sealed record LimitInfo(string Label, double Fraction, DateTimeOffset? ResetsAt);

/// <summary>
/// Parses the payload from GET https://api.anthropic.com/api/oauth/usage. Ports the ps1
/// reference's Get-Limits/Get-ScopedLimits/ConvertTo-Utilization exactly, including the
/// whole-payload percent-vs-fraction scale detection and the scoped `limits[]` entries.
/// </summary>
public static class UsageParser
{
    public static IReadOnlyList<LimitInfo> Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var session = JsonHelpers.TryGet(root, "five_hour");
        var weekly = JsonHelpers.TryGet(root, "seven_day_oauth_apps") ?? JsonHelpers.TryGet(root, "seven_day");

        var limitEntries = new List<JsonElement>();
        if (JsonHelpers.TryGet(root, "limits") is JsonElement limitsEl && limitsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in limitsEl.EnumerateArray())
            {
                limitEntries.Add(entry);
            }
        }

        var percentScale = DetectPercentScale(session, weekly, limitEntries);

        var result = new List<LimitInfo>();

        if (session is JsonElement sessionEl)
        {
            AddBucketLimit(result, sessionEl, "Session (5-hour)", percentScale);
        }

        if (weekly is JsonElement weeklyEl)
        {
            AddBucketLimit(result, weeklyEl, "Weekly (7-day)", percentScale);
        }

        AddScopedLimits(result, limitEntries, percentScale);

        return result;
    }

    private static bool DetectPercentScale(JsonElement? session, JsonElement? weekly, List<JsonElement> limitEntries)
    {
        // One payload speaks one convention: percent (37.0, 1.0) or fraction (0.37). Any
        // value >= 1 anywhere in the payload means percent-scaled, so 1.0 renders as 1%,
        // not 100%.
        if (session is JsonElement s && IsAtLeastOne(JsonHelpers.TryGet(s, "utilization")))
        {
            return true;
        }

        if (weekly is JsonElement w && IsAtLeastOne(JsonHelpers.TryGet(w, "utilization")))
        {
            return true;
        }

        foreach (var entry in limitEntries)
        {
            if (IsAtLeastOne(JsonHelpers.TryGet(entry, "percent")))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsAtLeastOne(JsonElement? value)
    {
        var raw = JsonHelpers.RawString(value);
        return raw is not null
            && double.TryParse(raw.Trim().TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out var n)
            && n >= 1;
    }

    private static void AddBucketLimit(List<LimitInfo> result, JsonElement bucket, string label, bool percentScale)
    {
        var fraction = ConvertUtilization(JsonHelpers.RawString(JsonHelpers.TryGet(bucket, "utilization")), percentScale);
        if (fraction < 0)
        {
            return;
        }

        var resetsAt = JsonHelpers.ParseTimestamp(JsonHelpers.RawString(JsonHelpers.TryGet(bucket, "resets_at")));
        result.Add(new LimitInfo(label, fraction, resetsAt));
    }

    // The payload's `limits` array is the only place a model-scoped allowance shows up (a
    // weekly window only one model draws from, say) — the matching flat keys stayed behind
    // at null, so reading buckets alone would silently drop a limit the account is really
    // spending against.
    private static void AddScopedLimits(List<LimitInfo> result, List<JsonElement> limitEntries, bool percentScale)
    {
        var seen = new HashSet<string>();

        foreach (var entry in limitEntries)
        {
            if (JsonHelpers.TryGet(entry, "scope") is not JsonElement scope)
            {
                continue;
            }

            if (JsonHelpers.TryGet(scope, "model") is not JsonElement model)
            {
                continue;
            }

            var name = JsonHelpers.GetStringOrEmpty(model, "display_name");
            if (string.IsNullOrEmpty(name))
            {
                name = JsonHelpers.GetStringOrEmpty(model, "id");
            }

            name = name.Trim();

            var kind = JsonHelpers.GetStringOrEmpty(entry, "kind").Trim();

            if (name.Length == 0 || !seen.Add($"{name}|{kind}"))
            {
                continue;
            }

            var fraction = ConvertUtilization(JsonHelpers.RawString(JsonHelpers.TryGet(entry, "percent")), percentScale);
            if (fraction < 0)
            {
                continue;
            }

            var window = ScopedWindow(kind);
            var title = window.Length > 0 ? $"{name} {window}" : name;
            var resetsAt = JsonHelpers.ParseTimestamp(JsonHelpers.RawString(JsonHelpers.TryGet(entry, "resets_at")));

            result.Add(new LimitInfo(title, fraction, resetsAt));
        }
    }

    private static string ScopedWindow(string kind)
    {
        var t = kind.ToLowerInvariant();
        if (t.Contains("month", StringComparison.Ordinal))
        {
            return "Monthly";
        }

        if (t.Contains("week", StringComparison.Ordinal) || t.Contains("day", StringComparison.Ordinal))
        {
            return "Weekly";
        }

        if (t.Contains("hour", StringComparison.Ordinal) || t.Contains("session", StringComparison.Ordinal))
        {
            return "Session";
        }

        return string.Empty;
    }

    /// <summary>Negative/unparseable values return -1 so the caller can skip them.</summary>
    private static double ConvertUtilization(string? raw, bool percentScale)
    {
        if (raw is null)
        {
            return -1;
        }

        if (!double.TryParse(raw.Trim().TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out var n))
        {
            return -1;
        }

        if (n < 0)
        {
            return -1;
        }

        return percentScale || n > 1 ? Math.Min(1.0, n / 100.0) : Math.Min(1.0, n);
    }
}
