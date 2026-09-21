using System;
using System.Threading;
using System.Threading.Tasks;
using ClaudeUsage.Core;

namespace ClaudeUsage.Services;

/// <summary>
/// Single owner of the extension's live state: the latest usage snapshot and the (memoized,
/// background-scanned) local transcript stats. Owned by the CommandsProvider and shared by
/// the Dock band and the detail page. Nothing here ever blocks the caller: the poll loop and
/// the transcript scan both run on background tasks.
/// </summary>
internal sealed partial class UsageStore : IDisposable
{
    private static readonly TimeSpan ManualRefreshThrottle = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan LocalStatsMemoizeWindow = TimeSpan.FromSeconds(60);

    // 429 backoff ladder: 3 -> 6 -> 12 -> 15 min (cap), reset after a non-429 response.
    private static readonly TimeSpan[] BackoffSteps =
    [
        TimeSpan.FromMinutes(3),
        TimeSpan.FromMinutes(6),
        TimeSpan.FromMinutes(12),
        TimeSpan.FromMinutes(15),
    ];

    private readonly SettingsManager _settings;
    private readonly object _restartLock = new();

    private int _pollInProgress;
    private int _localStatsScanInProgress;
    private int _backoffStepIndex = -1;

    private DateTimeOffset _lastPollAttempt = DateTimeOffset.MinValue;
    private DateTimeOffset? _backoffUntil;

    private volatile LocalStats? _localStats;
    private DateTimeOffset _localStatsUpdatedAt = DateTimeOffset.MinValue;

    private CancellationTokenSource? _timerCts;

    private volatile UsageSnapshot _snapshot = UsageSnapshot.Initial;

    public UsageStore(SettingsManager settings)
    {
        _settings = settings;
        _settings.RefreshIntervalChanged += (_, _) => RestartTimer(pollImmediately: false);
        RestartTimer(pollImmediately: true);
    }

    /// <summary>Raised whenever the snapshot or the local stats change.</summary>
    public event EventHandler? Changed;

    public UsageSnapshot Snapshot => _snapshot;

    /// <summary>
    /// Returns the memoized local stats, kicking off exactly one background rescan if the
    /// cached value is missing or older than 60s. Never blocks: the caller gets whatever is
    /// cached (possibly null on first call) and a Changed event follows once the scan lands.
    /// </summary>
    public LocalStats? GetLocalStats()
    {
        if (_localStats is null || DateTimeOffset.UtcNow - _localStatsUpdatedAt > LocalStatsMemoizeWindow)
        {
            StartLocalStatsScanIfNeeded();
        }

        return _localStats;
    }

    /// <summary>Manual refresh; ignored if the last attempt was under 60s ago.</summary>
    public void RefreshNow()
    {
        if (DateTimeOffset.UtcNow - _lastPollAttempt < ManualRefreshThrottle)
        {
            return;
        }

        _ = PollAsync(honorBackoff: false);
    }

    public void Dispose()
    {
        lock (_restartLock)
        {
            _timerCts?.Cancel();
            _timerCts?.Dispose();
            _timerCts = null;
        }
    }

    private void RestartTimer(bool pollImmediately)
    {
        lock (_restartLock)
        {
            _timerCts?.Cancel();
            _timerCts?.Dispose();

            var cts = new CancellationTokenSource();
            _timerCts = cts;
            _ = RunTimerLoopAsync(_settings.RefreshInterval, pollImmediately, cts.Token);
        }
    }

    private async Task RunTimerLoopAsync(TimeSpan interval, bool pollImmediately, CancellationToken token)
    {
        using var timer = new PeriodicTimer(interval);
        try
        {
            if (pollImmediately)
            {
                await PollAsync(honorBackoff: true).ConfigureAwait(false);
            }

            while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
            {
                await PollAsync(honorBackoff: true).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when the interval changes or the store is disposed.
        }
    }

    private async Task PollAsync(bool honorBackoff)
    {
        if (honorBackoff && _backoffUntil is DateTimeOffset until && DateTimeOffset.UtcNow < until)
        {
            return; // Still cooling down from a previous 429; let this tick pass quietly.
        }

        if (Interlocked.CompareExchange(ref _pollInProgress, 1, 0) != 0)
        {
            return; // A poll is already in flight.
        }

        try
        {
            _lastPollAttempt = DateTimeOffset.UtcNow;

            var dir = ClaudePaths.ConfigDir();
            var login = CredentialReader.Read(dir);
            var previous = _snapshot;

            UsageSnapshot next;
            if (string.IsNullOrEmpty(login.Token))
            {
                next = BuildFailureSnapshot(
                    previous,
                    login.Plan,
                    UsageStatus.AuthMissing,
                    "Run `claude auth login`, or start Claude Code once, to restore authoritative usage.");
                ResetBackoff();
            }
            else if (login.ExpiresAtMs > 0 && DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() > login.ExpiresAtMs)
            {
                // Expired locally: skip the network call entirely.
                next = BuildFailureSnapshot(
                    previous,
                    login.Plan,
                    UsageStatus.AuthExpired,
                    "Sign-in expired. Start Claude Code once to refresh the token. Local stats are still shown.");
                ResetBackoff();
            }
            else
            {
                var version = ClaudeCodeVersion.Get(dir);
                var apiResult = await UsageApiClient.GetUsageAsync(login.Token, version).ConfigureAwait(false);
                next = MapApiResult(previous, login.Plan, apiResult);
            }

            _snapshot = next;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // An escaped exception would end the timer loop and freeze the band on stale
            // data, so any surprise (odd payload, unreadable file) becomes a visible error.
            _snapshot = BuildFailureSnapshot(
                _snapshot, string.Empty, UsageStatus.Error,
                "Couldn't read Claude usage. Local stats are still shown.");
        }
        finally
        {
            Interlocked.Exchange(ref _pollInProgress, 0);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private UsageSnapshot MapApiResult(UsageSnapshot previous, string plan, UsageApiResult result)
    {
        switch (result.Outcome)
        {
            case UsageApiOutcome.Ok:
                ResetBackoff();
                return new UsageSnapshot
                {
                    Plan = plan,
                    Limits = result.Limits,
                    Status = UsageStatus.Ok,
                    HelpText = string.Empty,
                    UpdatedAt = DateTimeOffset.UtcNow,
                    IsStale = false,
                };

            case UsageApiOutcome.NoLimits:
                ResetBackoff();
                return BuildFailureSnapshot(
                    previous, plan, UsageStatus.NoLimits,
                    "Anthropic's usage endpoint returned no limits. Local stats are still shown.");

            case UsageApiOutcome.Unauthorized:
                ResetBackoff();
                return BuildFailureSnapshot(
                    previous, plan, UsageStatus.AuthExpired,
                    "Sign-in expired. Start Claude Code once to refresh the token. Local stats are still shown.");

            case UsageApiOutcome.RateLimited:
                AdvanceBackoff();
                return BuildFailureSnapshot(
                    previous, plan, UsageStatus.RateLimited,
                    "Anthropic's usage endpoint is rate limiting checks right now. Local stats are still shown.");

            case UsageApiOutcome.HttpError:
                ResetBackoff();
                return BuildFailureSnapshot(
                    previous, plan, UsageStatus.Error,
                    $"Anthropic's usage endpoint returned status {result.HttpStatusCode}. Local stats are still shown.");

            case UsageApiOutcome.NetworkError:
            default:
                ResetBackoff();
                return BuildFailureSnapshot(
                    previous, plan, UsageStatus.Error,
                    "Couldn't reach Anthropic's usage endpoint. Local stats are still shown.");
        }
    }

    /// <summary>Keeps the last known-good limits (marked stale) instead of blanking the UI on a transient failure.</summary>
    private static UsageSnapshot BuildFailureSnapshot(UsageSnapshot previous, string plan, UsageStatus status, string helpText) =>
        new()
        {
            Plan = string.IsNullOrEmpty(plan) ? previous.Plan : plan,
            Limits = previous.Limits,
            Status = status,
            HelpText = helpText,
            UpdatedAt = previous.UpdatedAt,
            IsStale = previous.Limits.Count > 0,
        };

    private void AdvanceBackoff()
    {
        _backoffStepIndex = Math.Min(_backoffStepIndex + 1, BackoffSteps.Length - 1);
        _backoffUntil = DateTimeOffset.UtcNow + BackoffSteps[_backoffStepIndex];
    }

    private void ResetBackoff()
    {
        _backoffStepIndex = -1;
        _backoffUntil = null;
    }

    private void StartLocalStatsScanIfNeeded()
    {
        if (Interlocked.CompareExchange(ref _localStatsScanInProgress, 1, 0) != 0)
        {
            return;
        }

        _ = Task.Run(() =>
        {
            try
            {
                var dir = ClaudePaths.ConfigDir();
                _localStats = TranscriptScanner.Scan(dir);
                _localStatsUpdatedAt = DateTimeOffset.UtcNow;
            }
            catch (Exception)
            {
                // Keep the previous stats; the next page open retries the scan.
                return;
            }
            finally
            {
                Interlocked.Exchange(ref _localStatsScanInProgress, 0);
            }

            Changed?.Invoke(this, EventArgs.Empty);
        });
    }
}
