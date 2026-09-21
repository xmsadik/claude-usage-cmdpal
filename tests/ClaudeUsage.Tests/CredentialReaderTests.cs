using ClaudeUsage.Core;
using Xunit;

namespace ClaudeUsage.Tests;

/// <summary>
/// Exercises CredentialReader against synthetic fixture files only — never the real
/// ~/.claude/.credentials.json — so no live token is ever read, logged, or asserted on.
/// </summary>
public sealed class CredentialReaderTests : IDisposable
{
    private readonly string _dir;

    public CredentialReaderTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ClaudeUsageTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void MaxTier_ProducesMaxPlanLabel()
    {
        WriteCredentials("""
            {
              "claudeAiOauth": {
                "accessToken": "synthetic-fixture-token",
                "expiresAt": 9999999999999,
                "rateLimitTier": "max_5x",
                "subscriptionType": "max"
              }
            }
            """);

        var login = CredentialReader.Read(_dir);

        Assert.Equal("Max 5x", login.Plan);
        Assert.Equal("synthetic-fixture-token", login.Token);
        Assert.Equal(9999999999999, login.ExpiresAtMs);
    }

    [Fact]
    public void ProSubscription_IsCapitalized()
    {
        WriteCredentials("""
            {
              "claudeAiOauth": {
                "accessToken": "synthetic-fixture-token",
                "expiresAt": 1234567890000,
                "subscriptionType": "pro"
              }
            }
            """);

        var login = CredentialReader.Read(_dir);

        Assert.Equal("Pro", login.Plan);
    }

    [Fact]
    public void MissingFile_ReturnsEmptyLoginWithoutThrowing()
    {
        var login = CredentialReader.Read(_dir);

        Assert.Equal(Login.Empty, login);
    }

    [Fact]
    public void GarbledFile_ReturnsEmptyLoginWithoutThrowing()
    {
        File.WriteAllText(Path.Combine(_dir, ".credentials.json"), "{ not valid json");

        var login = CredentialReader.Read(_dir);

        Assert.Equal(Login.Empty, login);
    }

    private void WriteCredentials(string json) =>
        File.WriteAllText(Path.Combine(_dir, ".credentials.json"), json);
}
