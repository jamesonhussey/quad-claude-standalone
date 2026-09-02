namespace QuadClaude.Config;

/// <summary>
/// Per-repo overrides loaded from a <c>.quadclaude.json</c> file at the repo root.
/// All fields optional — missing values fall back to the global <see cref="QuadConfig"/>.
/// </summary>
/// <example>
/// <code>
/// {
///   "devServerSubdir": "apps/main-app",
///   "devServerCommand": "npm run dev -- --port {port}"
/// }
/// </code>
/// </example>
public class RepoConfig
{
    /// <summary>Subdir within the repo where the dev server should run (e.g. "apps/main-app" for monorepos).</summary>
    public string? DevServerSubdir { get; set; }

    /// <summary>Command template for the dev server. Supports the {port} placeholder.</summary>
    public string? DevServerCommand { get; set; }
}
