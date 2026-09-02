using QuadClaude.Config;

namespace QuadClaude.Monday;

/// <summary>
/// Resolves the monday.com API token. Config wins; falls back to the
/// MONDAY_API_TOKEN environment variable so it never has to be committed.
/// </summary>
public static class MondayAuth
{
    public static string? ResolveToken(QuadConfig config)
    {
        if (!string.IsNullOrWhiteSpace(config.MondayApiToken))
            return config.MondayApiToken.Trim();
        return Environment.GetEnvironmentVariable("MONDAY_API_TOKEN")?.Trim();
    }
}
