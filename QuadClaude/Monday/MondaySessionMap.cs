using System.IO;
using System.Text.Json;

namespace QuadClaude.Monday;

/// <summary>
/// One Claude Code session bound to a monday.com action item.
/// </summary>
public class MondaySessionEntry
{
    public string SessionId { get; set; } = "";
    public string? Cwd { get; set; }       // Windows path the session runs in
    public string? Branch { get; set; }    // git branch (from the item's Branch column)
    public string? Title { get; set; }     // item name, for display/debugging
    public bool Started { get; set; }       // false until first launch → use --session-id, then --resume
}

/// <summary>
/// Persists the pulseId → Claude session mapping so each action item keeps its
/// own conversation across QuadClaude restarts. Stored as monday-sessions.json
/// in AppData. The whole point of the panel: click a task, resume its session.
/// </summary>
public class MondaySessionMap
{
    private static readonly string Path_ = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "QuadClaude", "monday-sessions.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public Dictionary<string, MondaySessionEntry> ByPulse { get; set; } = new();

    public static MondaySessionMap Load()
    {
        try
        {
            if (File.Exists(Path_))
            {
                var json = File.ReadAllText(Path_);
                var map = JsonSerializer.Deserialize<Dictionary<string, MondaySessionEntry>>(json, JsonOptions);
                if (map != null) return new MondaySessionMap { ByPulse = map };
            }
        }
        catch { /* corrupt/locked — start fresh rather than crash the panel */ }
        return new MondaySessionMap();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path_)!);
            var json = JsonSerializer.Serialize(ByPulse, JsonOptions);
            var tmp = Path_ + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, Path_, overwrite: true);
        }
        catch { /* best effort */ }
    }

    /// <summary>
    /// Return the existing session for a pulse, or mint a new one (fresh UUID,
    /// Started=false) bound to the given cwd/branch. Does not save — caller saves
    /// after a successful launch.
    /// </summary>
    public MondaySessionEntry GetOrCreate(string pulseId, string? cwd, string? branch, string? title)
    {
        if (ByPulse.TryGetValue(pulseId, out var existing))
        {
            // Keep the session, but refresh cwd/branch/title if they were unset.
            existing.Cwd ??= cwd;
            existing.Branch ??= branch;
            existing.Title ??= title;
            return existing;
        }

        var entry = new MondaySessionEntry
        {
            SessionId = Guid.NewGuid().ToString(),
            Cwd = cwd,
            Branch = branch,
            Title = title,
            Started = false,
        };
        ByPulse[pulseId] = entry;
        return entry;
    }
}
