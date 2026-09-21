using System.Text.Json;
using System.Text.RegularExpressions;

namespace ClaudeUsage.Core;

/// <summary>The parts of ~/.claude/.credentials.json we ever look at.</summary>
public sealed record Login(string Token, long ExpiresAtMs, string Plan)
{
    public static readonly Login Empty = new(string.Empty, 0, string.Empty);
}

/// <summary>
/// Reads ~/.claude/.credentials.json. Ports the ps1 reference's Get-OAuthLogin: only the
/// access token, its expiry, and a display-safe plan label ever leave this file. The token
/// itself is never logged, written back to disk, or included in any exception message.
/// </summary>
public static partial class CredentialReader
{
    public static Login Read(string dir)
    {
        var path = Path.Combine(dir, ".credentials.json");
        if (!File.Exists(path))
        {
            return Login.Empty;
        }

        try
        {
            // Claude Code rewrites this file on token refresh; share read/write so a
            // concurrent write never causes us to blow up mid-poll.
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var doc = JsonDocument.Parse(stream);

            if (JsonHelpers.TryGet(doc.RootElement, "claudeAiOauth") is not JsonElement login)
            {
                return Login.Empty;
            }

            var token = JsonHelpers.GetStringOrEmpty(login, "accessToken");
            var expiresAt = JsonHelpers.ToInt64Lenient(JsonHelpers.TryGet(login, "expiresAt"));
            var tier = JsonHelpers.GetStringOrEmpty(login, "rateLimitTier");
            var subscription = JsonHelpers.GetStringOrEmpty(login, "subscriptionType");

            var plan = string.Empty;
            var tierMatch = MaxTierRegex().Match(tier);
            if (tierMatch.Success)
            {
                plan = "Max " + tierMatch.Groups[1].Value;
            }
            else if (!string.IsNullOrEmpty(subscription))
            {
                plan = char.ToUpperInvariant(subscription[0]) + subscription[1..];
            }

            return new Login(token, expiresAt, plan);
        }
        catch
        {
            // Missing/garbled file -> empty Login, never throw.
            return Login.Empty;
        }
    }

    [GeneratedRegex("max_(\\d+x)")]
    private static partial Regex MaxTierRegex();
}
