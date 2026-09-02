using System.IO;

namespace QuadClaude.Config;

public static class PathHelper
{
    /// <summary>
    /// Convert Windows path to MSYS/Git Bash path: C:\foo\bar → /c/foo/bar
    /// </summary>
    public static string ToMsysPath(string windowsPath)
    {
        if (string.IsNullOrEmpty(windowsPath)) return windowsPath;

        // Handle drive letter: C:\ → /c/
        if (windowsPath.Length >= 2 && windowsPath[1] == ':')
        {
            var drive = char.ToLower(windowsPath[0]);
            windowsPath = $"/{drive}{windowsPath[2..]}";
        }

        return windowsPath.Replace('\\', '/');
    }

    /// <summary>
    /// Escape backslashes for JSON string embedding: C:\foo → C:\\\\foo
    /// (double-escaped because the JSON string itself needs escaping)
    /// </summary>
    public static string ToJsonEscaped(string windowsPath)
    {
        return windowsPath.Replace(@"\", @"\\\\");
    }

    /// <summary>
    /// Single-level escape for JSON values: C:\foo → C:\\foo
    /// </summary>
    public static string ToJsonValue(string windowsPath)
    {
        return windowsPath.Replace(@"\", @"\\");
    }

    /// <summary>
    /// Convert MSYS path back to Windows path: /c/foo/bar → C:\foo\bar
    /// </summary>
    public static string ToWindowsPath(string msysPath)
    {
        if (string.IsNullOrEmpty(msysPath)) return msysPath;

        // Handle /c/ → C:\
        if (msysPath.Length >= 3 && msysPath[0] == '/' && msysPath[2] == '/')
        {
            var drive = char.ToUpper(msysPath[1]);
            msysPath = $"{drive}:{msysPath[2..]}";
        }

        return msysPath.Replace('/', '\\');
    }

    /// <summary>
    /// Instance name — lets a second "dev" QuadClaude run alongside the real one
    /// without colliding on state files or terminal windows. Set via the
    /// QUADCLAUDE_INSTANCE env var; defaults to "QuadClaude".
    /// </summary>
    public static string InstanceName =>
        Environment.GetEnvironmentVariable("QUADCLAUDE_INSTANCE") is { Length: > 0 } n
            ? n
            : "QuadClaude";

    /// <summary>This instance's AppData state dir, e.g. %APPDATA%\QuadClaude[-Dev].</summary>
    public static string AppDataDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), InstanceName);

    /// <summary>The default instance's AppData dir, used as a config fallback.</summary>
    public static string BaseAppDataDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "QuadClaude");

    /// <summary>
    /// Windows Terminal window-group name for a quad. The default instance keeps
    /// the bare "quad-N" name (unchanged behavior); other instances are prefixed
    /// so they don't grab each other's windows.
    /// </summary>
    public static string WtWindowName(int quadIndex)
        => InstanceName == "QuadClaude" ? $"quad-{quadIndex}" : $"{InstanceName.ToLowerInvariant()}-quad-{quadIndex}";

    /// <summary>
    /// Get the user's home directory (cross-platform aware).
    /// </summary>
    public static string HomeDir =>
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>
    /// Get the .claude directory path.
    /// </summary>
    public static string ClaudeDir =>
        Path.Combine(HomeDir, ".claude");

    /// <summary>
    /// Get the Claude settings.json path.
    /// </summary>
    public static string ClaudeSettingsPath =>
        Path.Combine(ClaudeDir, "settings.json");
}
