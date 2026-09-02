using System.IO;
using System.Text.Json;
using QuadClaude.Config;

namespace QuadClaude.Data;

public record TokenUsagePoint(
    long InputTokens,
    long OutputTokens,
    long CacheReadTokens,
    long CacheCreationTokens,
    DateTime Timestamp);

public record TranscriptData(
    string? AiTitle,
    List<TokenUsagePoint> UsagePoints,
    long FileOffset);

public static class SessionDataService
{
    private static readonly string ClaudeDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");

    public static string NormalizePath(string path)
    {
        // Convert MSYS paths (/c/Projects/...) to Windows paths (C:\Projects\...)
        if (path.Length >= 3 && path[0] == '/' && path[2] == '/')
            path = $"{char.ToUpper(path[1])}:{path[2..]}";
        return path.Replace('/', '\\').TrimEnd('\\');
    }

    public static string CwdToProjectDir(string cwd)
    {
        var normalized = NormalizePath(cwd);
        return normalized.Replace('\\', '-').Replace(':', '-');
    }

    public static string? FindSessionIdForCwd(string cwd)
    {
        var sessionsDir = Path.Combine(ClaudeDir, "sessions");
        if (!Directory.Exists(sessionsDir)) return null;

        var normalizedCwd = NormalizePath(cwd);
        string? bestSessionId = null;
        long bestTimestamp = 0;

        foreach (var file in Directory.EnumerateFiles(sessionsDir, "*.json"))
        {
            try
            {
                using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var doc = JsonDocument.Parse(stream);
                var root = doc.RootElement;

                if (!root.TryGetProperty("cwd", out var cwdProp)) continue;
                var sessionCwd = cwdProp.GetString();
                if (sessionCwd == null) continue;

                if (!string.Equals(NormalizePath(sessionCwd), normalizedCwd,
                    StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!root.TryGetProperty("sessionId", out var sidProp)) continue;
                var sid = sidProp.GetString();
                if (sid == null) continue;

                long ts = 0;
                if (root.TryGetProperty("updatedAt", out var updProp))
                    ts = updProp.GetInt64();
                else if (root.TryGetProperty("startedAt", out var startProp))
                    ts = startProp.GetInt64();

                if (ts > bestTimestamp)
                {
                    bestTimestamp = ts;
                    bestSessionId = sid;
                }
            }
            catch { }
        }

        return bestSessionId;
    }

    /// <summary>
    /// Like <see cref="FindSessionIdForCwd"/>, but excludes sessions already claimed by
    /// OTHER quads (via their quad-*.cwd.json state files) so two quads sharing a cwd
    /// don't latch onto each other's session. Returns the newest UNCLAIMED session id
    /// matching the cwd, or null.
    /// </summary>
    public static string? FindSessionIdForQuad(int quadIndex, string cwd)
    {
        var claimedByOthers = ClaimedByOtherQuads(quadIndex);

        var sessionsDir = Path.Combine(ClaudeDir, "sessions");
        if (!Directory.Exists(sessionsDir)) return null;

        var normalizedCwd = NormalizePath(cwd);
        string? bestSessionId = null;
        long bestTimestamp = 0;

        foreach (var file in Directory.EnumerateFiles(sessionsDir, "*.json"))
        {
            try
            {
                using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var doc = JsonDocument.Parse(stream);
                var root = doc.RootElement;

                if (!root.TryGetProperty("cwd", out var cwdProp)) continue;
                var sessionCwd = cwdProp.GetString();
                if (sessionCwd == null) continue;

                if (!string.Equals(NormalizePath(sessionCwd), normalizedCwd,
                    StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!root.TryGetProperty("sessionId", out var sidProp)) continue;
                var sid = sidProp.GetString();
                if (sid == null) continue;

                // Skip any session another quad is already showing.
                if (claimedByOthers.Contains(sid)) continue;

                long ts = 0;
                if (root.TryGetProperty("updatedAt", out var updProp))
                    ts = updProp.GetInt64();
                else if (root.TryGetProperty("startedAt", out var startProp))
                    ts = startProp.GetInt64();

                if (ts > bestTimestamp)
                {
                    bestTimestamp = ts;
                    bestSessionId = sid;
                }
            }
            catch { }
        }

        return bestSessionId;
    }

    /// <summary>
    /// Session ids currently claimed by quads OTHER than <paramref name="quadIndex"/>,
    /// read from their quad-*.cwd.json state files in AppData. Used to avoid two quads
    /// sharing a cwd bleeding each other's session/summary.
    /// </summary>
    private static HashSet<string> ClaimedByOtherQuads(int quadIndex)
    {
        var claimed = new HashSet<string>();
        try
        {
            var dir = PathHelper.AppDataDir;
            if (!Directory.Exists(dir)) return claimed;

            var selfFile = $"quad-{quadIndex}.cwd.json";
            foreach (var file in Directory.EnumerateFiles(dir, "quad-*.cwd.json"))
            {
                if (string.Equals(Path.GetFileName(file), selfFile, StringComparison.OrdinalIgnoreCase))
                    continue;
                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(file));
                    if (doc.RootElement.TryGetProperty("sessionId", out var sidProp))
                    {
                        var sid = sidProp.GetString();
                        if (!string.IsNullOrEmpty(sid)) claimed.Add(sid);
                    }
                }
                catch { /* file being rewritten or malformed — skip it */ }
            }
        }
        catch { }
        return claimed;
    }

    public static string? FindTranscriptPath(string sessionId, string cwd)
    {
        var projectDir = CwdToProjectDir(cwd);
        var projectsDir = Path.Combine(ClaudeDir, "projects");
        if (!Directory.Exists(projectsDir)) return null;

        foreach (var dir in Directory.EnumerateDirectories(projectsDir))
        {
            var dirName = Path.GetFileName(dir);
            if (!string.Equals(dirName, projectDir, StringComparison.OrdinalIgnoreCase))
                continue;

            var transcriptPath = Path.Combine(dir, sessionId + ".jsonl");
            if (File.Exists(transcriptPath))
                return transcriptPath;
        }

        return null;
    }

    public static TranscriptData ParseTranscript(string path, long lastOffset = 0)
    {
        string? aiTitle = null;
        var usagePoints = new List<TokenUsagePoint>();

        ParseJsonlFile(path, ref aiTitle, usagePoints);

        // Also parse subagent transcripts
        var sessionDir = Path.Combine(
            Path.GetDirectoryName(path)!,
            Path.GetFileNameWithoutExtension(path),
            "subagents");
        if (Directory.Exists(sessionDir))
        {
            foreach (var subFile in Directory.EnumerateFiles(sessionDir, "*.jsonl"))
                ParseJsonlFile(subFile, ref aiTitle, usagePoints);
        }

        // Sort by timestamp for the graph
        usagePoints.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));

        return new TranscriptData(aiTitle, usagePoints, 0);
    }

    private static void ParseJsonlFile(string path, ref string? aiTitle, List<TokenUsagePoint> usagePoints)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);

            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var root = doc.RootElement;

                    if (!root.TryGetProperty("type", out var typeProp)) continue;
                    var type = typeProp.GetString();

                    if (type == "ai-title")
                    {
                        if (root.TryGetProperty("aiTitle", out var titleProp))
                            aiTitle = titleProp.GetString();
                    }
                    else if (type == "assistant")
                    {
                        if (root.TryGetProperty("message", out var msg)
                            && msg.TryGetProperty("usage", out var usage))
                        {
                            long input = 0, output = 0, cacheRead = 0, cacheCreate = 0;

                            if (usage.TryGetProperty("input_tokens", out var inp))
                                input = inp.GetInt64();
                            if (usage.TryGetProperty("output_tokens", out var outp))
                                output = outp.GetInt64();
                            if (usage.TryGetProperty("cache_read_input_tokens", out var cr))
                                cacheRead = cr.GetInt64();
                            if (usage.TryGetProperty("cache_creation_input_tokens", out var cc))
                                cacheCreate = cc.GetInt64();

                            var timestamp = DateTime.UtcNow;
                            if (root.TryGetProperty("timestamp", out var tsProp))
                            {
                                if (tsProp.ValueKind == JsonValueKind.String
                                    && DateTime.TryParse(tsProp.GetString(), out var parsed))
                                    timestamp = parsed.ToUniversalTime();
                            }

                            usagePoints.Add(new TokenUsagePoint(
                                input, output, cacheRead, cacheCreate, timestamp));
                        }
                    }
                }
                catch { }
            }
        }
        catch { }
    }
}
