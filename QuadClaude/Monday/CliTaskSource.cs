using System.Diagnostics;
using System.IO;
using System.Text.Json;
using QuadClaude.Config;

namespace QuadClaude.Monday;

/// <summary>
/// Fetches board items by shelling out to the node CLI script
/// (monday-tasks.mjs), which prints a normalized JSON array. This is the "CLI"
/// half of the side-by-side comparison.
/// </summary>
public class CliTaskSource : IMondayTaskSource
{
    private readonly QuadConfig _config;

    public CliTaskSource(QuadConfig config) => _config = config;

    public string Name => "CLI";

    public async Task<List<MondayTask>> FetchAsync(CancellationToken ct = default)
    {
        var stdout = await RunNodeAsync(new[]
        {
            "--board", _config.MondayBoardId.ToString(),
            "--group", _config.MondayGroupId,
            "--host", _config.MondayHost,
        }, ct);

        // Trust a valid JSON array on stdout even if the process exited non-zero
        // (node on Windows can emit harmless libuv teardown noise after output).
        if (stdout.StartsWith('[')) return ParseOutput(stdout);
        throw new InvalidOperationException($"CLI fetch returned no task list: {Trunc(stdout)}");
    }

    public async Task<MondayTaskDetail> FetchDetailAsync(string pulseId, CancellationToken ct = default)
    {
        var stdout = await RunNodeAsync(new[] { "--item", pulseId }, ct);
        if (!stdout.StartsWith('{')) return MondayTaskDetail.Empty;

        using var doc = JsonDocument.Parse(stdout);
        var root = doc.RootElement;
        var description = Str(root, "description");
        var updates = new List<MondayUpdate>();
        if (root.TryGetProperty("updates", out var ups) && ups.ValueKind == JsonValueKind.Array)
        {
            foreach (var u in ups.EnumerateArray())
            {
                var text = Str(u, "text");
                if (string.IsNullOrWhiteSpace(text)) continue;
                updates.Add(new MondayUpdate(Str(u, "author"), Str(u, "date"), text!));
            }
        }
        return new MondayTaskDetail(description, updates);
    }

    /// <summary>Run the node script with the given args + token, return trimmed stdout.</summary>
    private async Task<string> RunNodeAsync(string[] args, CancellationToken ct)
    {
        var token = MondayAuth.ResolveToken(_config);
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException(
                "No monday API token. Set mondayApiToken in config.json or the MONDAY_API_TOKEN env var.");

        var scriptPath = ResolveScriptPath();
        if (!File.Exists(scriptPath))
            throw new FileNotFoundException($"CLI fetch script not found: {scriptPath}");

        var psi = new ProcessStartInfo
        {
            FileName = "node",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add(scriptPath);
        foreach (var a in args) psi.ArgumentList.Add(a);
        psi.Environment["MONDAY_API_TOKEN"] = token;

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start node. Is node on PATH?");

        var stdoutTask = proc.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = proc.StandardError.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct);
        var stdout = (await stdoutTask).Trim();
        var stderr = await stderrTask;

        if (stdout.Length == 0)
            throw new InvalidOperationException($"CLI fetch failed (exit {proc.ExitCode}): {stderr.Trim()}");
        return stdout;
    }

    private static string Trunc(string s) => s.Length > 200 ? s[..200] + "…" : s;

    /// <summary>Parse the script's normalized JSON array into tasks.</summary>
    public static List<MondayTask> ParseOutput(string stdout)
    {
        var tasks = new List<MondayTask>();
        if (string.IsNullOrWhiteSpace(stdout)) return tasks;

        using var doc = JsonDocument.Parse(stdout);
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            tasks.Add(new MondayTask(
                Id: Str(item, "id") ?? "",
                Name: Str(item, "name") ?? "(untitled)",
                Status: Str(item, "status"),
                StatusColor: Str(item, "statusColor"),
                Priority: Str(item, "priority"),
                Branch: Str(item, "branch"),
                Owner: Str(item, "owner"),
                Group: Str(item, "group"),
                Url: Str(item, "url") ?? ""));
        }
        return tasks;
    }

    private const string ScriptName = "monday-tasks.mjs";

    private string ResolveScriptPath()
    {
        // 1) Alongside the configured setup dir (claude-launch.sh lives there too).
        if (!string.IsNullOrWhiteSpace(_config.SetupDir))
        {
            var p = Path.Combine(_config.SetupDir, ScriptName);
            if (File.Exists(p)) return p;
        }

        // 2) Walk up from the running exe — handles worktree/dev builds whose
        //    setup dir differs from the configured (installed) one.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
        {
            var p = Path.Combine(dir.FullName, ScriptName);
            if (File.Exists(p)) return p;
        }

        // 3) Last resort — return the configured path so the error names it.
        var fallback = !string.IsNullOrWhiteSpace(_config.SetupDir)
            ? _config.SetupDir
            : QuadLauncher.FindSetupDirFallback();
        return Path.Combine(fallback, ScriptName);
    }

    private static string? Str(JsonElement el, string prop)
        => el.TryGetProperty(prop, out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString()
            : null;
}
