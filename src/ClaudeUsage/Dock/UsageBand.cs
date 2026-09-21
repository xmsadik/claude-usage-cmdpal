using System;
using System.Globalization;
using System.Linq;
using ClaudeUsage.Core;
using ClaudeUsage.Services;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace ClaudeUsage;

/// <summary>The always-visible Dock item: the 5-hour session percentage and its reset countdown.</summary>
internal sealed partial class UsageBand : ListItem, IDisposable
{
    private const string SessionLabel = "Session (5-hour)";

    private readonly UsageStore _store;
    private readonly System.Timers.Timer _countdownTimer;

    public UsageBand(UsageStore store, UsageDetailPage detailPage)
        : base(detailPage)
    {
        _store = store;
        _store.Changed += OnStoreChanged;

        Render();

        // Re-renders only the countdown text between polls; never touches the network.
        _countdownTimer = new System.Timers.Timer(TimeSpan.FromSeconds(60)) { AutoReset = true };
        _countdownTimer.Elapsed += (_, _) => Render();
        _countdownTimer.Start();
    }

    public void Dispose()
    {
        _store.Changed -= OnStoreChanged;
        _countdownTimer.Stop();
        _countdownTimer.Dispose();
    }

    private void OnStoreChanged(object? sender, EventArgs e) => Render();

    private void Render()
    {
        var snapshot = _store.Snapshot;

        if (snapshot.Status is UsageStatus.AuthMissing or UsageStatus.AuthExpired)
        {
            Title = "auth";
            Icon = new IconInfo("⚠️");
            Subtitle = "Open Claude Code";
            return;
        }

        var session = snapshot.Limits.FirstOrDefault(l => l.Label == SessionLabel);
        if (session is null)
        {
            Title = "--%";
            Icon = new IconInfo(string.Empty);
            Subtitle = "Claude 5h";
            return;
        }

        var percent = (int)Math.Round(session.Fraction * 100, MidpointRounding.AwayFromZero);
        Title = percent.ToString(CultureInfo.InvariantCulture) + "%";
        Icon = new IconInfo(session.Fraction >= 0.9 ? "🔴" : session.Fraction >= 0.7 ? "🟡" : "🟢");
        Subtitle = snapshot.IsStale
            ? "5h · stale"
            : "5h · " + ShortCountdown(session.ResetsAt, DateTimeOffset.UtcNow);
    }

    private static string ShortCountdown(DateTimeOffset? resetsAt, DateTimeOffset now)
    {
        var text = Format.Countdown(resetsAt, now);
        if (text.StartsWith("resets in ", StringComparison.Ordinal))
        {
            return text["resets in ".Length..];
        }

        return text == "resets now" ? "now" : text;
    }
}
