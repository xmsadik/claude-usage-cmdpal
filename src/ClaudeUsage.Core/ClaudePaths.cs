namespace ClaudeUsage.Core;

/// <summary>Resolves the Claude Code config directory, matching the ps1 reference's Get-ClaudeDir.</summary>
public static class ClaudePaths
{
    public static string ConfigDir()
    {
        var configured = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        if (!string.IsNullOrEmpty(configured))
        {
            return configured;
        }

        var home = Environment.GetEnvironmentVariable("USERPROFILE") ?? string.Empty;
        return Path.Combine(home, ".claude");
    }
}
