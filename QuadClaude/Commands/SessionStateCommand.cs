using System.IO;
using System.Text.Json;
using QuadClaude.Config;

namespace QuadClaude.Commands;

/// <summary>
/// Records the live activity state of a quad's Claude session, written by hooks:
///   UserPromptSubmit / PreToolUse → "busy"
///   Stop                          → "done"
///   Notification permission_prompt → "needs-input"
///
/// The StatusWidget reads this per-quad file to drive the idle overlay's colour
/// and mode. This replaces parsing Claude's internal session files, whose format
/// is undocumented and changed between versions (the old "busy" status field is
/// gone in current Claude Code), silently disabling the working overlay.
///
/// Usage: QuadClaude.exe session-state (busy|done|needs-input)
/// </summary>
public static class SessionStateCommand
{
    private static readonly string[] Valid = ["busy", "done", "needs-input"];

    public static int Execute(string? state)
    {
        if (string.IsNullOrWhiteSpace(state) || Array.IndexOf(Valid, state) < 0)
            return 1;

        // Hooks inherit QUAD_INDEX from the launcher; fall back to a default file so
        // manually-run / unmapped sessions still record somewhere harmless.
        var quadIndex = Environment.GetEnvironmentVariable("QUAD_INDEX");
        var fileName = quadIndex != null
            ? $"session-state-quad-{quadIndex}.json"
            : "session-state-default.json";

        try
        {
            Directory.CreateDirectory(PathHelper.AppDataDir);
            var file = Path.Combine(PathHelper.AppDataDir, fileName);
            File.WriteAllText(file, JsonSerializer.Serialize(new { state }));
        }
        catch { /* transient IO — the next hook write will correct it */ }

        return 0;
    }
}
