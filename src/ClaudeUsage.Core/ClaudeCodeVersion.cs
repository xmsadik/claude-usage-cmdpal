using System.Text.Json;

namespace ClaudeUsage.Core;

/// <summary>
/// Reads the locally-installed Claude Code CLI version from .last-update-result.json, used
/// to build the User-Agent header the usage endpoint expects (community finding: requests
/// without it get 429'd far more often).
/// </summary>
public static class ClaudeCodeVersion
{
    private const string FallbackVersion = "2.1.0";

    public static string Get(string dir)
    {
        var path = Path.Combine(dir, ".last-update-result.json");
        if (!File.Exists(path))
        {
            return FallbackVersion;
        }

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var doc = JsonDocument.Parse(stream);
            var root = doc.RootElement;

            var versionTo = JsonHelpers.GetStringOrEmpty(root, "version_to");
            if (!string.IsNullOrEmpty(versionTo))
            {
                return versionTo;
            }

            var versionFrom = JsonHelpers.GetStringOrEmpty(root, "version_from");
            return !string.IsNullOrEmpty(versionFrom) ? versionFrom : FallbackVersion;
        }
        catch
        {
            return FallbackVersion;
        }
    }
}
