using System.Net;
using System.Net.Http.Headers;

namespace ClaudeUsage.Core;

/// <summary>Transport-level outcome of a single call to the usage endpoint.</summary>
public enum UsageApiOutcome
{
    Ok,
    Unauthorized,
    RateLimited,
    HttpError,
    NetworkError,
    NoLimits,
}

public sealed class UsageApiResult
{
    public required UsageApiOutcome Outcome { get; init; }

    public IReadOnlyList<LimitInfo> Limits { get; init; } = [];

    /// <summary>Set for Unauthorized/RateLimited/HttpError.</summary>
    public int? HttpStatusCode { get; init; }

    public static UsageApiResult Ok(IReadOnlyList<LimitInfo> limits) =>
        new() { Outcome = UsageApiOutcome.Ok, Limits = limits };

    public static UsageApiResult NoLimits() => new() { Outcome = UsageApiOutcome.NoLimits };

    public static UsageApiResult Unauthorized() =>
        new() { Outcome = UsageApiOutcome.Unauthorized, HttpStatusCode = 401 };

    public static UsageApiResult RateLimited() =>
        new() { Outcome = UsageApiOutcome.RateLimited, HttpStatusCode = 429 };

    public static UsageApiResult HttpError(int statusCode) =>
        new() { Outcome = UsageApiOutcome.HttpError, HttpStatusCode = statusCode };

    public static UsageApiResult NetworkError() => new() { Outcome = UsageApiOutcome.NetworkError };
}

/// <summary>
/// Calls GET https://api.anthropic.com/api/oauth/usage. One static HttpClient for the whole
/// process, per Microsoft guidance (avoids socket exhaustion from a client-per-call pattern).
/// The access token is used only in the Authorization header of this one request: never
/// logged, never written to disk, never surfaced in an exception message.
/// </summary>
public static class UsageApiClient
{
    private const string Endpoint = "https://api.anthropic.com/api/oauth/usage";

    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(15),
    };

    public static async Task<UsageApiResult> GetUsageAsync(
        string token,
        string claudeCodeVersion,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, Endpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.TryAddWithoutValidation("anthropic-beta", "oauth-2025-04-20");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.UserAgent.TryParseAdd($"claude-code/{claudeCodeVersion}");

            using var response = await HttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                return UsageApiResult.Unauthorized();
            }

            if ((int)response.StatusCode == 429)
            {
                return UsageApiResult.RateLimited();
            }

            if (!response.IsSuccessStatusCode)
            {
                return UsageApiResult.HttpError((int)response.StatusCode);
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var limits = UsageParser.Parse(body);
            return limits.Count == 0 ? UsageApiResult.NoLimits() : UsageApiResult.Ok(limits);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Caller-requested cancellation (e.g. shutdown): let it propagate.
            throw;
        }
        catch (OperationCanceledException)
        {
            // HttpClient's own 15s timeout surfaces as a TaskCanceledException.
            return UsageApiResult.NetworkError();
        }
        catch (HttpRequestException)
        {
            return UsageApiResult.NetworkError();
        }
        catch (System.Text.Json.JsonException)
        {
            // Successful response with an unparseable body: treat like "no data".
            return UsageApiResult.NoLimits();
        }
    }
}
