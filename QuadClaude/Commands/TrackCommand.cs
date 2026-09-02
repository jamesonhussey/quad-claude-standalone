using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace QuadClaude.Commands;

/// <summary>
/// Writes the current working directory and git branch to the quad's state file.
/// Called by Claude Code hooks (Stop) to keep StatusWidget up to date.
/// Reads QUAD_INDEX from the environment to know which quad to update.
/// Uses the process's current working directory (inherited from Claude Code).
/// </summary>
public static class TrackCommand
{
    public static int Execute()
    {
        // Only quads launched via claude-launch.sh export QUAD_INDEX. A Claude session
        // started outside the launcher (this repo's own VS Code chat, a Claude-Day
        // worker, etc.) has none — it must NOT default to "0", or its track hook would
        // clobber quad-0's cwd file with the wrong repo/branch and point that quad's
        // start-server button at the wrong directory. Route unmapped sessions to a
        // harmless default file no StatusWidget with a real index polls.
        var quadIndex = Environment.GetEnvironmentVariable("QUAD_INDEX") is { Length: > 0 } qi
            ? qi
            : "default";
        var appDataDir = QuadClaude.Config.PathHelper.AppDataDir;
        Directory.CreateDirectory(appDataDir);

        var cwd = Directory.GetCurrentDirectory();
        var project = Path.GetFileName(cwd);
        var branch = GetGitBranch(cwd);
        var sessionId = ReadSessionIdFromStdin();

        var state = new
        {
            cwd,
            project,
            branch,
            sessionId
        };

        var json = JsonSerializer.Serialize(state);
        var stateFile = Path.Combine(appDataDir, $"quad-{quadIndex}.cwd.json");
        var tmpFile = stateFile + ".tmp";

        try
        {
            File.WriteAllText(tmpFile, json);
            File.Move(tmpFile, stateFile, overwrite: true);
        }
        catch
        {
            // If atomic move fails, write directly
            try { File.WriteAllText(stateFile, json); } catch { }
        }

        return 0;
    }

    private static string? ReadSessionIdFromStdin()
    {
        try
        {
            if (Console.IsInputRedirected)
            {
                var json = Console.In.ReadToEnd();
                if (!string.IsNullOrEmpty(json))
                {
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("session_id", out var sid))
                        return sid.GetString();
                }
            }
        }
        catch { }
        return null;
    }

    private static string GetGitBranch(string dir)
    {
        // Fast path: read .git/HEAD directly (no process spawn)
        var gitDir = FindGitDir(dir);
        if (gitDir != null)
        {
            var headFile = Path.Combine(gitDir, "HEAD");
            if (File.Exists(headFile))
            {
                var head = File.ReadAllText(headFile).Trim();
                // "ref: refs/heads/main" → "main"
                if (head.StartsWith("ref: refs/heads/"))
                    return head["ref: refs/heads/".Length..];
                // Detached HEAD — resolve the base ref it points at for a clear label
                // (e.g. "staging (detached)") rather than a bare, ambiguous short sha.
                var shortSha = head.Length >= 7 ? head[..7] : head;
                return DescribeDetachedHead(dir, shortSha);
            }
        }

        // Fallback: run git
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = "symbolic-ref --short HEAD",
                WorkingDirectory = dir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            var proc = Process.Start(psi);
            if (proc != null)
            {
                var output = proc.StandardOutput.ReadToEnd().Trim();
                proc.WaitForExit(3000);
                if (proc.ExitCode == 0 && !string.IsNullOrEmpty(output))
                    return output;
            }
        }
        catch { }

        return "";
    }

    /// <summary>
    /// On a detached HEAD, resolve the base ref the commit sits on and return a clear
    /// label like "staging (detached)". Prefers staging/main/master when several refs
    /// point at HEAD; strips a leading "origin/". Falls back to "detached (&lt;short-sha&gt;)"
    /// when no ref points at HEAD or git fails. Mirrors the bash trackers.
    /// </summary>
    private static string DescribeDetachedHead(string dir, string shortSha)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = "for-each-ref --points-at HEAD --format=%(refname:short) refs/heads refs/remotes/origin",
                WorkingDirectory = dir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            var proc = Process.Start(psi);
            if (proc != null)
            {
                var output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit(3000);
                if (proc.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
                {
                    var names = output
                        .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Select(n => n.StartsWith("origin/") ? n["origin/".Length..] : n)
                        .Where(n => n.Length > 0)
                        .ToList();
                    if (names.Count > 0)
                    {
                        var preferred = names.FirstOrDefault(n => n is "staging" or "main" or "master")
                                        ?? names[0];
                        return $"{preferred} (detached)";
                    }
                }
            }
        }
        catch { }

        return $"detached ({shortSha})";
    }

    private static string? FindGitDir(string dir)
    {
        var current = new DirectoryInfo(dir);
        while (current != null)
        {
            var gitDir = Path.Combine(current.FullName, ".git");
            if (Directory.Exists(gitDir))
                return gitDir;
            // .git can also be a file (worktrees): "gitdir: /path/to/worktree"
            if (File.Exists(gitDir))
            {
                var content = File.ReadAllText(gitDir).Trim();
                if (content.StartsWith("gitdir: "))
                    return content["gitdir: ".Length..].Trim();
            }
            current = current.Parent;
        }
        return null;
    }
}
