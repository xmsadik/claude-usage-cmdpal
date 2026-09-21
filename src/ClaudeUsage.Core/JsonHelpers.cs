using System.Globalization;
using System.Text.Json;

namespace ClaudeUsage.Core;

/// <summary>
/// Small helpers shared by the JSON readers in this assembly. Everything here uses
/// <see cref="JsonDocument"/> / <see cref="JsonElement"/> directly (no reflection-based
/// (de)serialization), so it stays trim/AOT friendly.
/// </summary>
internal static class JsonHelpers
{
    /// <summary>Looks up a property, treating JSON null the same as "missing".</summary>
    public static JsonElement? TryGet(JsonElement obj, string name)
    {
        if (obj.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return obj.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null
            ? value
            : null;
    }

    public static string GetStringOrEmpty(JsonElement obj, string name)
    {
        var value = TryGet(obj, name);
        return value is JsonElement e && e.ValueKind == JsonValueKind.String
            ? e.GetString() ?? string.Empty
            : string.Empty;
    }

    /// <summary>
    /// Renders a number-or-string JSON value as the raw text used for parsing
    /// (e.g. utilization/percent fields that may arrive as 37.0 or "37%").
    /// </summary>
    public static string? RawString(JsonElement? el)
    {
        if (el is not JsonElement e)
        {
            return null;
        }

        return e.ValueKind switch
        {
            JsonValueKind.Number => e.GetRawText(),
            JsonValueKind.String => e.GetString(),
            _ => null,
        };
    }

    /// <summary>Ports the ps1 ConvertTo-Number helper: always cast through double, 0 on failure.</summary>
    public static long ToInt64Lenient(JsonElement? el)
    {
        var raw = RawString(el);
        if (raw is null)
        {
            return 0;
        }

        return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
            ? (long)d
            : 0;
    }

    /// <summary>
    /// Ports ConvertTo-ResetTime / ConvertTo-LocalDate's epoch handling: an all-digit string
    /// is epoch seconds or milliseconds (ms if &gt;= 1e12), otherwise an ISO-8601 string.
    /// Returns null when unparseable.
    /// </summary>
    public static DateTimeOffset? ParseTimestamp(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        raw = raw.Trim();
        if (raw.Length > 0 && IsAllDigits(raw))
        {
            if (!long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var num))
            {
                return null;
            }

            var ms = num;
            if (ms < 1_000_000_000_000L)
            {
                ms *= 1000;
            }

            return DateTimeOffset.FromUnixTimeMilliseconds(ms);
        }

        return DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dto)
            ? dto
            : null;
    }

    private static bool IsAllDigits(string s)
    {
        foreach (var c in s)
        {
            if (c is < '0' or > '9')
            {
                return false;
            }
        }

        return true;
    }
}
