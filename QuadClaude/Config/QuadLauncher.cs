using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using QuadClaude.Interop;

namespace QuadClaude.Config;

/// <summary>
/// Shared logic for launching and positioning individual quad terminals.
/// Used by LaunchCommand (all 4) and StatusWidget (single quad restart).
/// </summary>
public static class QuadLauncher
{
    public static string AppDataDir => PathHelper.AppDataDir;

    public static readonly string[] TerminalProcessNames = ["WindowsTerminal", "warp"];

    /// <summary>
    /// How many terminals a layout uses. Most layouts fill all four slots; "two-up"
    /// is a deliberate two-window mode. Single source of truth for launch and the
    /// runtime layout switch so they agree on which windows to touch.
    /// </summary>
    public static int WindowCountForLayout(string? layout) => layout == "two-up" ? 2 : 4;

    private static string FocusOrderFile => Path.Combine(AppDataDir, "focus-order.json");

    /// <summary>
    /// Which quad occupies which focus-layout slot. order[slot] = quad index; slot 0 is
    /// the big focus pane. Shared via a small file so any of the four widget processes
    /// can read/update it. Defaults to identity [0,1,2,3]; falls back to identity if the
    /// file is missing, malformed, or not a permutation of 0..3.
    /// </summary>
    public static int[] ReadFocusOrder()
    {
        try
        {
            if (File.Exists(FocusOrderFile))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(FocusOrderFile));
                if (doc.RootElement.TryGetProperty("order", out var arr) && arr.GetArrayLength() == 4)
                {
                    var order = new int[4];
                    int i = 0;
                    foreach (var e in arr.EnumerateArray()) order[i++] = e.GetInt32();
                    if (order.OrderBy(x => x).SequenceEqual([0, 1, 2, 3])) return order;
                }
            }
        }
        catch { /* missing/malformed — use default */ }
        return [0, 1, 2, 3];
    }

    public static void WriteFocusOrder(int[] order)
    {
        try
        {
            Directory.CreateDirectory(AppDataDir);
            File.WriteAllText(FocusOrderFile, JsonSerializer.Serialize(new { order }));
        }
        catch { /* transient IO — next write corrects it */ }
    }

    /// <summary>Reset focus order to the default identity mapping.</summary>
    public static void ResetFocusOrder() => WriteFocusOrder([0, 1, 2, 3]);

    /// <summary>
    /// Read a quad's live activity state ("busy" | "done" | "needs-input") from its
    /// hook-written state file. Any widget can query any quad this way. Defaults to
    /// "done" when the file is missing/unreadable.
    /// </summary>
    public static string ReadSessionState(int quadIndex)
    {
        try
        {
            var file = Path.Combine(AppDataDir, $"session-state-quad-{quadIndex}.json");
            if (File.Exists(file))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                if (doc.RootElement.TryGetProperty("state", out var s))
                {
                    var v = s.GetString();
                    if (v is "busy" or "needs-input" or "done") return v;
                }
            }
        }
        catch { /* being written — treat as done this cycle */ }
        return "done";
    }

    /// <summary>Is the quad's tracked terminal window still alive?</summary>
    public static bool QuadWindowLive(int quadIndex)
    {
        try
        {
            var file = Path.Combine(AppDataDir, $"quad-{quadIndex}.hwnd");
            if (!File.Exists(file)) return false;
            var h = new IntPtr(long.Parse(File.ReadAllText(file).Trim()));
            return NativeMethods.IsWindow(h);
        }
        catch { return false; }
    }

    /// <summary>
    /// Move <paramref name="quadIndex"/> into the big focus slot, preserving the relative
    /// order of the others (e.g. promoting quad 3 from [0,1,2,3] gives [3,0,1,2]).
    /// Persists and returns the new order.
    /// </summary>
    public static int[] PromoteToFocus(int quadIndex)
    {
        var order = ReadFocusOrder().ToList();
        if (!order.Remove(quadIndex)) return order.ToArray();
        order.Insert(0, quadIndex);
        var arr = order.ToArray();
        WriteFocusOrder(arr);
        return arr;
    }

    public static (int[] posX, int[] posY, int[] w, int[] h) GetGridPositions(QuadConfig? config = null)
    {
        var layout = config?.WindowLayout ?? "grid";
        var work = GetTargetMonitorWorkArea(config?.TargetMonitor ?? "largest");
        int screenW = work.Right - work.Left;
        int screenH = work.Bottom - work.Top;
        int x0 = work.Left;
        int y0 = work.Top;

        // "dual": two quads side-by-side on each of two monitors (half-width, full-
        // height per monitor). Falls back to the 2×2 grid if only one monitor exists.
        if (layout == "dual")
        {
            var monitors = GetAllMonitorWorkAreas();
            if (monitors.Count >= 2)
            {
                // Two largest monitors, ordered left-to-right so Q0/Q1 land on the
                // left screen and Q2/Q3 on the right.
                var pair = monitors.Take(2).OrderBy(m => m.Left).ToList();
                var m0 = pair[0];
                var m1 = pair[1];
                int w0 = (m0.Right - m0.Left) / 2;
                int h0 = m0.Bottom - m0.Top;
                int w1 = (m1.Right - m1.Left) / 2;
                int h1 = m1.Bottom - m1.Top;
                return (
                    [m0.Left, m0.Left + w0, m1.Left, m1.Left + w1],
                    [m0.Top,  m0.Top,       m1.Top,  m1.Top],
                    [w0, w0, w1, w1],
                    [h0, h0, h1, h1]);
            }
            // Single monitor — degrade gracefully to the 2×2 grid below.
        }

        // "two-up": two quads side-by-side on one monitor (each half-width, full-
        // height). LaunchCommand only spawns two windows for this layout; the last
        // two entries mirror the first two so any stray positioning stays on-screen.
        if (layout == "two-up")
        {
            int twoW = screenW / 2;
            return (
                [x0, x0 + twoW, x0, x0 + twoW],
                [y0, y0, y0, y0],
                [twoW, twoW, twoW, twoW],
                [screenH, screenH, screenH, screenH]);
        }

        // "rows": four quads stacked top-to-bottom (full-width, quarter-height).
        if (layout == "rows")
        {
            int rowH = screenH / 4;
            return (
                [x0, x0, x0, x0],
                [y0, y0 + rowH, y0 + rowH * 2, y0 + rowH * 3],
                [screenW, screenW, screenW, screenW],
                [rowH, rowH, rowH, rowH]);
        }

        if (layout == "focus")
        {
            int mainW = screenW / 2;
            int sideW = screenW / 4;
            int halfH = screenH / 2;
            // Slot geometry, indexed by slot: 0 = big, 1 = secondary tall, 2 = small
            // top-right, 3 = small bottom-right.
            int[] slotX = [x0, x0 + mainW, x0 + mainW + sideW, x0 + mainW + sideW];
            int[] slotY = [y0, y0, y0, y0 + halfH];
            int[] slotW = [mainW, sideW, sideW, sideW];
            int[] slotH = [screenH, screenH, halfH, halfH];

            // Which quad sits in which slot — driven by the focus order so quads can be
            // promoted/rotated at runtime. order[slot] = quad index. Default puts quad i
            // in slot i. Result is returned indexed by quad so callers stay index-based.
            var order = ReadFocusOrder();
            int[] posX = new int[4], posY = new int[4], fw = new int[4], fh = new int[4];
            for (int slot = 0; slot < 4; slot++)
            {
                int quad = order[slot];
                posX[quad] = slotX[slot];
                posY[quad] = slotY[slot];
                fw[quad] = slotW[slot];
                fh[quad] = slotH[slot];
            }
            return (posX, posY, fw, fh);
        }

        if (layout == "columns")
        {
            int colW = screenW / 4;
            return (
                [x0, x0 + colW, x0 + colW * 2, x0 + colW * 3],
                [y0, y0, y0, y0],
                [colW, colW, colW, colW],
                [screenH, screenH, screenH, screenH]);
        }

        int halfW = screenW / 2;
        int halfH2 = screenH / 2;
        return (
            [x0, x0 + halfW, x0, x0 + halfW],
            [y0, y0, y0 + halfH2, y0 + halfH2],
            [halfW, halfW, halfW, halfW],
            [halfH2, halfH2, halfH2, halfH2]);
    }

    /// <summary>
    /// Launch a single terminal for the given quad index.
    /// Returns the HWND of the new window, or IntPtr.Zero on failure.
    /// </summary>
    public static IntPtr LaunchSingleQuad(int quadIndex, QuadConfig? config = null)
    {
        config ??= QuadConfig.Load();

        string projectsDir = config?.ProjectsDir ?? Path.Combine(PathHelper.HomeDir, "Projects");
        string shellExe = config?.ShellExe ?? @"C:\Program Files\Git\bin\bash.exe";
        string shellType = config?.ShellType ?? "gitbash";
        string terminalProfile = config?.TerminalProfile ?? "Git Bash";
        string setupDir = config?.SetupDir ?? FindSetupDirFallback();
        string launchScript = PathHelper.ToMsysPath(Path.Combine(setupDir, "claude-launch.sh"));

        Directory.CreateDirectory(AppDataDir);

        // Snapshot existing windows
        var existingHandles = GetAllTerminalWindowHandles();

        // Launch terminal
        var args = BuildTerminalArgs(shellType, terminalProfile, projectsDir, shellExe, launchScript, quadIndex);

        Process.Start(new ProcessStartInfo
        {
            FileName = "wt",
            Arguments = args,
            UseShellExecute = true
        });

        // Wait for new window
        IntPtr newHWnd = IntPtr.Zero;
        var deadline = DateTime.Now.AddSeconds(10);
        while (DateTime.Now < deadline)
        {
            Thread.Sleep(300);
            var currentHandles = GetAllTerminalWindowHandles();
            foreach (var handle in currentHandles)
            {
                if (!existingHandles.Contains(handle))
                {
                    newHWnd = handle;
                    break;
                }
            }
            if (newHWnd != IntPtr.Zero) break;
        }

        if (newHWnd == IntPtr.Zero) return IntPtr.Zero;

        // Snap to grid position
        var (posX, posY, w, h) = GetGridPositions(config);
        Thread.Sleep(200);
        NativeMethods.ShowWindow(newHWnd, NativeMethods.SW_RESTORE);
        Thread.Sleep(150);
        NativeMethods.MoveWindow(newHWnd, posX[quadIndex], posY[quadIndex], w[quadIndex], h[quadIndex], true);

        // Store HWND
        File.WriteAllText(Path.Combine(AppDataDir, $"quad-{quadIndex}.hwnd"), newHWnd.ToInt64().ToString());

        return newHWnd;
    }

    /// <summary>
    /// Swap a quad into a monday.com task: write a one-shot task handoff file,
    /// close the quad's current terminal, then relaunch it. claude-launch.sh
    /// consumes the handoff to cd into the task dir, checkout its branch, and
    /// start/resume the task's Claude session. Returns the new terminal HWND.
    /// Intended to be called on a background STA thread (it blocks ~1s + launch).
    /// </summary>
    public static IntPtr LaunchTaskInQuad(
        int quadIndex, string? cwdWindows, string? branch,
        string sessionId, bool resume, string? prompt = null, QuadConfig? config = null)
    {
        config ??= QuadConfig.Load();

        WriteTaskFile(quadIndex, cwdWindows, branch, sessionId, resume, prompt);

        // Close the quad's current terminal (session history is already on disk,
        // so this is lossless except for an in-flight, mid-response turn).
        var oldHWnd = ReadQuadHWnd(quadIndex);
        if (oldHWnd != IntPtr.Zero)
            CloseTerminal(oldHWnd);

        // Give the old window a beat to tear down before reusing the wt -w slot.
        Thread.Sleep(1000);

        var newHWnd = LaunchSingleQuad(quadIndex, config);
        if (newHWnd != IntPtr.Zero)
            SpawnStatusWidget(newHWnd, quadIndex);
        return newHWnd;
    }

    /// <summary>
    /// Write the one-shot task handoff for a quad. dir is normalized to MSYS
    /// form for the bash launch script. Deleted by claude-launch.sh once read.
    /// </summary>
    public static void WriteTaskFile(
        int quadIndex, string? cwdWindows, string? branch, string sessionId, bool resume, string? prompt = null)
    {
        Directory.CreateDirectory(AppDataDir);
        var msysDir = string.IsNullOrWhiteSpace(cwdWindows) ? "" : PathHelper.ToMsysPath(cwdWindows);
        var task = new
        {
            dir = msysDir,
            branch = branch ?? "",
            sessionId,
            resume,
        };
        var json = JsonSerializer.Serialize(task);
        var path = Path.Combine(AppDataDir, $"quad-{quadIndex}.task.json");
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, json);
        File.Move(tmp, path, overwrite: true);

        // The seed prompt rides in a sibling file (avoids JSON escaping of
        // multi-line/quoted text). claude-launch.sh reads + deletes it.
        var promptPath = Path.Combine(AppDataDir, $"quad-{quadIndex}.prompt.txt");
        if (!string.IsNullOrWhiteSpace(prompt))
            File.WriteAllText(promptPath, prompt);
        else if (File.Exists(promptPath))
            File.Delete(promptPath); // clear any stale prompt (e.g. on resume)
    }

    public static IntPtr ReadQuadHWnd(int quadIndex)
    {
        try
        {
            var file = Path.Combine(AppDataDir, $"quad-{quadIndex}.hwnd");
            if (!File.Exists(file)) return IntPtr.Zero;
            var val = long.Parse(File.ReadAllText(file).Trim());
            var hWnd = new IntPtr(val);
            return NativeMethods.IsWindow(hWnd) ? hWnd : IntPtr.Zero;
        }
        catch { return IntPtr.Zero; }
    }

    /// <summary>
    /// Spawn a StatusWidget process for a given quad.
    /// </summary>
    public static void SpawnStatusWidget(IntPtr hWnd, int quadIndex)
    {
        var exePath = Environment.ProcessPath ?? "QuadClaude.exe";
        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = $"status --hwnd {hWnd.ToInt64()} --quad {quadIndex}",
            UseShellExecute = false,
        };
        // Explicitly carry the instance so the widget (and the Monday panel it
        // opens) read/write the SAME state dir as the grid that spawned it.
        psi.Environment["QUADCLAUDE_INSTANCE"] = PathHelper.InstanceName;
        Process.Start(psi);
    }

    public static void CloseTerminal(IntPtr hWnd, bool force = false)
    {
        if (hWnd == IntPtr.Zero || !NativeMethods.IsWindow(hWnd)) return;

        if (force)
        {
            NativeMethods.GetWindowThreadProcessId(hWnd, out uint pid);
            try { Process.GetProcessById((int)pid).Kill(entireProcessTree: true); } catch { }
        }
        else
        {
            NativeMethods.PostMessage(hWnd, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        }
    }

    public static void StartDevServerTab(int quadIndex, string command, QuadConfig? config = null, string? workingDir = null)
    {
        config ??= QuadConfig.Load();
        string profile = config?.TerminalProfile ?? "Git Bash";
        string shellExe = config?.ShellExe ?? @"C:\Program Files\Git\bin\bash.exe";

        // Prefer the quad's actual tracked directory (workingDir). An "extra" quad (one
        // beyond the configured grid) can be sitting in a worktree that no longer matches
        // the {base}-{n} mapping for its index, so the live cwd is what's correct. This is
        // fed in as the base-dir override, then still run through the .quadclaude.json
        // subdir anchoring so repos that serve from a subdirectory keep working.
        var wtDir = ResolveDevServerCwd(config!, quadIndex, workingDir);

        var escapedCmd = command.Replace(";", "\\;");
        var args = $"-w {PathHelper.WtWindowName(quadIndex)} new-tab -p \"{profile}\" -d \"{wtDir}\" \"{shellExe}\" -c \"{escapedCmd}\\; exec bash -i\" ; focus-tab -t 0";
        Process.Start(new ProcessStartInfo
        {
            FileName = "wt",
            Arguments = args,
            UseShellExecute = true
        });
    }

    /// <summary>
    /// Resolves the base dir for dev server work (before any subdir append).
    /// Priority: (0) explicit live baseDirOverride if it exists (e.g. the quad's tracked
    /// cwd), (1) absolute DevServerCwd override, (2) tracked cwd from quad-N.cwd.json,
    /// (3) worktree dir (legacy), (4) ProjectsDir.
    /// </summary>
    public static string ResolveDevServerBaseDir(QuadConfig config, int quadIndex, string? baseDirOverride = null)
    {
        if (!string.IsNullOrWhiteSpace(baseDirOverride))
        {
            var win = PathHelper.ToWindowsPath(baseDirOverride);
            if (Directory.Exists(win)) return NormalizeToRepoRoot(win);
        }

        if (!string.IsNullOrWhiteSpace(config.DevServerCwd) && Path.IsPathRooted(config.DevServerCwd))
            return config.DevServerCwd; // explicit user override — respect it verbatim

        var tracked = ReadTrackedCwd(quadIndex);
        if (!string.IsNullOrWhiteSpace(tracked))
            return NormalizeToRepoRoot(tracked);

        return ResolveWorktreeDir(config, quadIndex) ?? config.ProjectsDir;
    }

    /// <summary>
    /// Walk up from a directory to its repo/worktree root — the nearest ancestor containing a
    /// `.git` entry (a directory in a normal clone, a FILE in a git worktree). The quad's tracked
    /// cwd is often a subdirectory inside the worktree (e.g. the agent cd'd into apps/main-app), so
    /// using it raw as the dev-server base dir makes a subdir/`cd` in the command apply twice
    /// (…/apps/main-app/apps/main-app → cd fails → the server never starts). Anchoring to the
    /// worktree root fixes that whether the quad is tracked at the root or inside a subdir. Falls
    /// back to the input dir when no `.git` is found (non-git dirs behave as before).
    /// </summary>
    private static string NormalizeToRepoRoot(string dir)
    {
        try
        {
            var d = new DirectoryInfo(dir);
            for (int i = 0; i < 24 && d != null; i++, d = d.Parent)
            {
                var git = Path.Combine(d.FullName, ".git");
                if (Directory.Exists(git) || File.Exists(git)) return d.FullName;
            }
        }
        catch { /* unreadable path — fall back to the input */ }
        return dir;
    }

    /// <summary>
    /// Resolves the final cwd for the dev server tab. Combines the base dir with
    /// the repo's per-repo subdir (.quadclaude.json) if present, otherwise the global
    /// DevServerSubdir. Result is normalized to Windows path form.
    /// </summary>
    public static string ResolveDevServerCwd(QuadConfig config, int quadIndex, string? baseDirOverride = null)
    {
        var baseDir = ResolveDevServerBaseDir(config, quadIndex, baseDirOverride);
        var (repo, repoRoot) = ReadRepoConfig(baseDir);
        var subdir = repo?.DevServerSubdir ?? config.DevServerSubdir;

        // Anchor subdir to the directory the .quadclaude.json lives in, not the tracked
        // cwd — otherwise relaunching from a child dir (e.g. apps/main-app) doubles the path.
        var combined = string.IsNullOrWhiteSpace(subdir)
            ? baseDir
            : Path.Combine(repoRoot ?? baseDir, subdir);

        // Canonicalize: forward slashes in subdir inputs would leave a mixed-separator
        // path that Windows Terminal's -d flag rejects.
        return combined.Replace('/', '\\');
    }

    /// <summary>
    /// Resolves the dev server command for a given quad. Repo-local .quadclaude.json
    /// overrides global DevServerCommand. {port} is substituted.
    /// </summary>
    public static string ResolveDevServerCommand(QuadConfig config, int quadIndex, int port)
    {
        var baseDir = ResolveDevServerBaseDir(config, quadIndex);
        var (repo, _) = ReadRepoConfig(baseDir);
        var template = repo?.DevServerCommand
            ?? config.DevServerCommand
            ?? "npm run dev -- --port {port}";
        return template.Replace("{port}", port.ToString());
    }

    /// <summary>
    /// Walks up from <paramref name="startDir"/> looking for a `.quadclaude.json` file.
    /// Returns the parsed repo config and the directory it was found in, or (null, null)
    /// if none found. Stops at drive root or after a sane depth limit.
    /// </summary>
    public static (RepoConfig? config, string? root) ReadRepoConfig(string? startDir)
    {
        if (string.IsNullOrWhiteSpace(startDir)) return (null, null);
        try
        {
            var dir = new DirectoryInfo(startDir);
            for (int i = 0; i < 12 && dir != null; i++, dir = dir.Parent)
            {
                var file = Path.Combine(dir.FullName, ".quadclaude.json");
                if (!File.Exists(file)) continue;
                var json = File.ReadAllText(file);
                return (JsonSerializer.Deserialize<RepoConfig>(json, RepoConfigJsonOptions), dir.FullName);
            }
        }
        catch { /* malformed or unreadable — fall back to global config */ }
        return (null, null);
    }

    private static readonly JsonSerializerOptions RepoConfigJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private static string? ReadTrackedCwd(int quadIndex)
    {
        try
        {
            var path = Path.Combine(AppDataDir,
                quadIndex >= 0 ? $"quad-{quadIndex}.cwd.json" : "quad-default.cwd.json");
            if (!File.Exists(path)) return null;
            var json = File.ReadAllText(path);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("cwd", out var cwdProp))
                return NormalizeWindowsPath(cwdProp.GetString());
        }
        catch { /* file being rewritten or malformed — skip */ }
        return null;
    }

    /// <summary>
    /// Normalize a path that may have been written by either the C# TrackCommand
    /// (Windows form: C:\foo\bar) or the bash track-cwd.sh script (msys form: /c/foo/bar).
    /// Returns a canonical Windows path with backslashes.
    /// </summary>
    private static string? NormalizeWindowsPath(string? p)
    {
        if (string.IsNullOrWhiteSpace(p)) return p;
        // msys/Git Bash: /c/foo/bar → C:\foo\bar
        var m = System.Text.RegularExpressions.Regex.Match(p, @"^/([a-zA-Z])/(.*)$");
        if (m.Success)
            p = $"{char.ToUpper(m.Groups[1].Value[0])}:\\{m.Groups[2].Value.Replace('/', '\\')}";
        return p.Replace('/', '\\');
    }

    public static void StopDevServer(int port)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c for /f \"tokens=5\" %a in ('netstat -aon ^| findstr :{port} ^| findstr LISTENING') do taskkill /F /PID %a",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            Process.Start(psi);
        }
        catch { }
    }

    public static string BuildTerminalArgs(string shellType, string profile, string projectsDir, string shellExe, string launchScript, int quadIndex)
    {
        var win = PathHelper.WtWindowName(quadIndex);
        var inst = PathHelper.InstanceName; // passed to claude-launch.sh as $2 so it uses the right state dir
        return shellType switch
        {
            "gitbash" => $"-w {win} -p \"{profile}\" -d \"{projectsDir}\" \"{shellExe}\" --login \"{launchScript}\" {quadIndex} {inst}",
            "wsl" => $"-w {win} -p \"{profile}\" -- wsl bash -l \"{launchScript}\" {quadIndex} {inst}",
            "powershell" => $"-w {win} -p \"{profile}\" -d \"{projectsDir}\" pwsh -NoExit -Command \"$env:QUAD_INDEX={quadIndex}; $env:QUADCLAUDE_INSTANCE='{inst}'; claude\"",
            _ => $"-w {win} -p \"{profile}\" -d \"{projectsDir}\" \"{shellExe}\" --login \"{launchScript}\" {quadIndex} {inst}"
        };
    }

    public static string? ResolveWorktreeDir(QuadConfig config, int quadIndex)
    {
        if (config.WorktreeBase == null) return null;

        // Every quad — including Quad 1 (index 0) — resolves to its own worktree, never
        // the bare base repo, so no quad ever drives git in the shared base checkout.
        int quadNum = quadIndex + 1;
        var name = config.WorktreePattern
            .Replace("{base}", config.WorktreeBase)
            .Replace("{n}", quadNum.ToString());

        return Path.IsPathRooted(name) ? name : Path.Combine(config.ProjectsDir, name);
    }

    public static string FindSetupDirFallback()
    {
        var exeDir = AppContext.BaseDirectory;
        var candidate = Path.GetFullPath(Path.Combine(exeDir, "..", ".."));
        if (File.Exists(Path.Combine(candidate, "claude-launch.sh")))
            return candidate;
        return exeDir;
    }

    /// <summary>
    /// Calculate a good font size for the target monitor.
    /// Baseline: 14pt at 1080p, scales proportionally with vertical resolution.
    /// Clamps between 10 and 18 to stay readable.
    /// </summary>
    public static int CalculateFontSize(string targetMonitor = "largest")
    {
        var work = GetTargetMonitorWorkArea(targetMonitor);
        int quadHeight = (work.Bottom - work.Top) / 2; // each quad is half the screen height

        // Scale: 11pt baseline at 580px quad height (1920x1200 laptop)
        // Scales proportionally: smaller screens get smaller, bigger get bigger
        // 768p laptop (quad ~350px) → ~7, clamp to 9
        // 1080p (quad ~520px) → ~10
        // 1200p (quad ~580px) → ~11 (baseline)
        // 1440p (quad ~700px) → ~13
        // 4K (quad ~1040px) → ~20, clamp to 16
        double fontSize = quadHeight / 580.0 * 11.0;
        return Math.Clamp((int)Math.Round(fontSize), 9, 16);
    }

    /// <summary>
    /// Update the Windows Terminal font size for the matching profile.
    /// Finds the profile by name in the user's WT settings and sets the font size.
    /// </summary>
    public static void UpdateTerminalFontSize(int fontSize, string profileName)
    {
        // Find WT settings — try Store install first, then scoop/manual
        string? settingsPath = FindWtSettingsPath();
        if (settingsPath == null || !File.Exists(settingsPath)) return;

        try
        {
            var json = File.ReadAllText(settingsPath);
            var root = JsonNode.Parse(json);
            if (root == null) return;

            var profiles = root["profiles"]?["list"]?.AsArray();
            if (profiles == null) return;

            bool updated = false;
            foreach (var profile in profiles)
            {
                var name = profile?["name"]?.GetValue<string>();
                if (name != null && name.Equals(profileName, StringComparison.OrdinalIgnoreCase))
                {
                    // Set or update the font object
                    var fontObj = profile!["font"]?.AsObject();
                    if (fontObj == null)
                    {
                        fontObj = new JsonObject { ["size"] = fontSize };
                        profile["font"] = fontObj;
                    }
                    else
                    {
                        fontObj["size"] = fontSize;
                    }
                    updated = true;
                    break;
                }
            }

            if (updated)
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                File.WriteAllText(settingsPath, root.ToJsonString(options));
            }
        }
        catch { /* don't break launch if WT settings can't be updated */ }
    }

    private static string? FindWtSettingsPath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        // Store install
        var storePath = Path.Combine(localAppData,
            "Packages", "Microsoft.WindowsTerminal_8wekyb3d8bbwe", "LocalState", "settings.json");
        if (File.Exists(storePath)) return storePath;

        // Scoop / manual install
        var scoopPath = Path.Combine(localAppData, "Microsoft", "Windows Terminal", "settings.json");
        if (File.Exists(scoopPath)) return scoopPath;

        return null;
    }

    public static RECT GetTargetMonitorWorkArea(string targetMonitor = "largest")
    {
        var monitors = new List<MONITORINFO>();
        NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMonitor, IntPtr hdc, ref RECT rc, IntPtr data) =>
        {
            var mi = new MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>() };
            if (NativeMethods.GetMonitorInfo(hMonitor, ref mi))
                monitors.Add(mi);
            return true;
        }, IntPtr.Zero);

        if (monitors.Count == 0)
        {
            var wa = System.Windows.SystemParameters.WorkArea;
            return new RECT { Left = (int)wa.Left, Top = (int)wa.Top, Right = (int)(wa.Left + wa.Width), Bottom = (int)(wa.Top + wa.Height) };
        }

        if (targetMonitor == "primary")
        {
            foreach (var m in monitors)
                if ((m.dwFlags & 0x1) != 0) return m.rcWork;
        }
        else if (targetMonitor == "secondary")
        {
            foreach (var m in monitors)
                if ((m.dwFlags & 0x1) == 0) return m.rcWork;
        }

        // "largest" or fallback
        MONITORINFO best = monitors[0];
        long bestArea = 0;
        foreach (var m in monitors)
        {
            long area = (long)(m.rcWork.Right - m.rcWork.Left) * (m.rcWork.Bottom - m.rcWork.Top);
            if (area > bestArea)
            {
                bestArea = area;
                best = m;
            }
        }

        return best.rcWork;
    }

    /// <summary>
    /// All monitor work areas, largest first. Used by multi-monitor layouts
    /// (e.g. "dual") that need to place quads across more than one screen.
    /// Falls back to a single entry (the primary work area) if enumeration fails.
    /// </summary>
    public static List<RECT> GetAllMonitorWorkAreas()
    {
        var monitors = new List<MONITORINFO>();
        NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMonitor, IntPtr hdc, ref RECT rc, IntPtr data) =>
        {
            var mi = new MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>() };
            if (NativeMethods.GetMonitorInfo(hMonitor, ref mi))
                monitors.Add(mi);
            return true;
        }, IntPtr.Zero);

        if (monitors.Count == 0)
        {
            var wa = System.Windows.SystemParameters.WorkArea;
            return [new RECT { Left = (int)wa.Left, Top = (int)wa.Top, Right = (int)(wa.Left + wa.Width), Bottom = (int)(wa.Top + wa.Height) }];
        }

        return monitors
            .OrderByDescending(m => (long)(m.rcWork.Right - m.rcWork.Left) * (m.rcWork.Bottom - m.rcWork.Top))
            .Select(m => m.rcWork)
            .ToList();
    }

    public static HashSet<IntPtr> GetAllTerminalWindowHandles()
    {
        var handles = new HashSet<IntPtr>();
        NativeMethods.EnumWindows((hWnd, _) =>
        {
            if (!NativeMethods.IsWindowVisible(hWnd)) return true;
            NativeMethods.GetWindowThreadProcessId(hWnd, out uint pid);
            try
            {
                var proc = Process.GetProcessById((int)pid);
                if (TerminalProcessNames.Any(n => proc.ProcessName.Equals(n, StringComparison.OrdinalIgnoreCase)))
                    handles.Add(hWnd);
            }
            catch { }
            return true;
        }, IntPtr.Zero);
        return handles;
    }
}
