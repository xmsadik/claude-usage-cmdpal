namespace ClaudeUsage.Core;

/// <summary>Application-level status for the last usage snapshot (drives Dock/detail-page UI).</summary>
public enum UsageStatus
{
    /// <summary>Limits were fetched successfully.</summary>
    Ok,

    /// <summary>No token in .credentials.json.</summary>
    AuthMissing,

    /// <summary>Token present but locally expired, or the API rejected it (401).</summary>
    AuthExpired,

    /// <summary>The usage endpoint returned 429.</summary>
    RateLimited,

    /// <summary>Any other HTTP or network failure.</summary>
    Error,

    /// <summary>The call succeeded but the payload had no limits in it.</summary>
    NoLimits,
}

/// <summary>
/// The latest known state of the account's usage limits, as held by the extension's
/// UsageStore and rendered by <see cref="MarkdownReport"/>.
/// </summary>
public sealed class UsageSnapshot
{
    public required string Plan { get; init; }

    public required IReadOnlyList<LimitInfo> Limits { get; init; }

    public required UsageStatus Status { get; init; }

    /// <summary>User-facing explanation shown alongside a non-Ok status.</summary>
    public required string HelpText { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }

    /// <summary>True when Limits/Plan are the last known-good values, not a fresh fetch.</summary>
    public required bool IsStale { get; init; }

    public static UsageSnapshot Initial { get; } = new()
    {
        Plan = string.Empty,
        Limits = [],
        Status = UsageStatus.NoLimits,
        HelpText = string.Empty,
        UpdatedAt = DateTimeOffset.MinValue,
        IsStale = false,
    };
}
