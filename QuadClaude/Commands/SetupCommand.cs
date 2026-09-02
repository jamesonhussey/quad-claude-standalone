using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using QuadClaude.Config;

namespace QuadClaude.Commands;

public static class SetupCommand
{
    public static int Execute()
    {
        PrintHeader();

        if (QuadConfig.Exists)
        {
            Console.WriteLine("  Existing configuration found. This will overwrite it.");
            if (!Confirm("  Continue?")) return 0;
            Console.WriteLine();
        }

        var config = new QuadConfig
        {
            SetupDir = FindSetupDir()
        };

        // Step 1: Shell / Terminal
        ConfigureShell(config);

        // Step 2: Projects Directory
        ConfigureProjectsDir(config);

        // Step 3: Layout
        ConfigureLayout(config);

        // Step 4: Sound Notifications
        ConfigureSounds(config);

        // Step 5: Permission Mode
        ConfigurePermissionMode(config);

        // Step 6: Allow-List
        ConfigureAllowList(config);

        // Step 7: Review & Apply
        return ReviewAndApply(config);
    }

    // ────────────────────────────────────────────────────────────
    //  Step 1: Shell
    // ────────────────────────────────────────────────────────────

    private static void ConfigureShell(QuadConfig config)
    {
        PrintStep(1, 7, "Shell / Terminal");
        Console.WriteLine("  QuadClaude launches terminals to run Claude Code in.");
        Console.WriteLine("  Which shell do you use?");
        Console.WriteLine();

        var gitBashPath = DetectGitBash();
        var gitBashLabel = gitBashPath != null
            ? $"Git Bash  (recommended)  --detected: {gitBashPath}"
            : "Git Bash  (recommended)  --not detected, enter path";

        Console.WriteLine($"  1) {gitBashLabel}");
        Console.WriteLine("  2) WSL / Bash");
        Console.WriteLine("  3) PowerShell");
        Console.WriteLine("  4) Other (enter path)");
        Console.WriteLine();

        var choice = ReadChoice(1, 4, 1);

        switch (choice)
        {
            case 1:
                config.ShellType = "gitbash";
                config.TerminalProfile = "Git Bash";
                config.ShellExe = gitBashPath ?? ReadPath("  Path to bash.exe: ");
                break;
            case 2:
                config.ShellType = "wsl";
                config.TerminalProfile = "Ubuntu"; // common default
                config.ShellExe = "wsl.exe";
                Console.Write("  WSL distro profile name in Windows Terminal [Ubuntu]: ");
                var distro = Console.ReadLine()?.Trim();
                if (!string.IsNullOrEmpty(distro)) config.TerminalProfile = distro;
                break;
            case 3:
                config.ShellType = "powershell";
                config.TerminalProfile = "PowerShell";
                config.ShellExe = "pwsh.exe";
                break;
            case 4:
                config.ShellType = "other";
                config.ShellExe = ReadPath("  Path to shell executable: ");
                Console.Write("  Windows Terminal profile name: ");
                config.TerminalProfile = Console.ReadLine()?.Trim() ?? "Default";
                break;
        }

        Console.WriteLine($"  > Shell: {config.ShellType} ({config.ShellExe})");
        Console.WriteLine();
    }

    // ────────────────────────────────────────────────────────────
    //  Step 2: Projects Directory
    // ────────────────────────────────────────────────────────────

    private static void ConfigureProjectsDir(QuadConfig config)
    {
        PrintStep(2, 7, "Projects Directory");
        Console.WriteLine("  This is where QuadClaude looks for your repos when you open a quad.");
        Console.WriteLine("  Each quadrant will let you pick a project from this directory.");
        Console.WriteLine("  Think of it as your workspace root --the folder that contains");
        Console.WriteLine("  all the repos and projects you want to work on with Claude.");
        Console.WriteLine();

        var candidates = DetectProjectsDirs();
        if (candidates.Count > 0)
        {
            Console.WriteLine("  Detected project directories:");
            for (int i = 0; i < candidates.Count; i++)
            {
                var repoCount = CountGitRepos(candidates[i]);
                var repoHint = repoCount > 0 ? $"  ({repoCount} repo{(repoCount == 1 ? "" : "s")} found)" : "";
                Console.WriteLine($"    {i + 1}) {candidates[i]}{repoHint}");
            }
            Console.WriteLine($"    {candidates.Count + 1}) Other (enter path)");
            Console.WriteLine();

            var choice = ReadChoice(1, candidates.Count + 1, 1);
            if (choice <= candidates.Count)
            {
                config.ProjectsDir = candidates[choice - 1];
            }
            else
            {
                config.ProjectsDir = ReadPath("  Full path to projects directory: ");
            }
        }
        else
        {
            Console.WriteLine("  No project directories detected automatically.");
            config.ProjectsDir = ReadPath("  Full path to projects directory: ");
        }

        if (!Directory.Exists(config.ProjectsDir))
        {
            if (Confirm($"  '{config.ProjectsDir}' doesn't exist. Create it?"))
                Directory.CreateDirectory(config.ProjectsDir);
        }

        Console.WriteLine($"  > Projects: {config.ProjectsDir}");
        Console.WriteLine();
    }

    // ────────────────────────────────────────────────────────────
    //  Step 3: Layout
    // ────────────────────────────────────────────────────────────

    private static void ConfigureLayout(QuadConfig config)
    {
        PrintStep(3, 7, "Layout");
        Console.WriteLine("  How do you want to use the four quadrants?");
        Console.WriteLine();
        Console.WriteLine("  1) Multi-Project  (recommended)");
        Console.WriteLine("     Each quad opens a project picker. Best for multiple repos.");
        Console.WriteLine();
        Console.WriteLine("  2) Single Repo, Four Worktrees");
        Console.WriteLine("     One repo + 3 git worktrees. Best for deep focus on one codebase.");
        Console.WriteLine();
        Console.WriteLine("  3) Hybrid");
        Console.WriteLine("     Mix of repos and worktrees. Picker shows everything.");
        Console.WriteLine();
        Console.WriteLine("  4) Dedicated Roles");
        Console.WriteLine("     All quads open same project, each with a named role.");
        Console.WriteLine();

        var choice = ReadChoice(1, 4, 1);

        switch (choice)
        {
            case 1:
                config.Layout = "multi-project";
                break;

            case 2:
                config.Layout = "worktrees";
                ConfigureWorktrees(config);
                break;

            case 3:
                config.Layout = "hybrid";
                Console.WriteLine("  Hybrid uses the project picker --set up worktrees manually");
                Console.WriteLine("  alongside your other repos in the projects directory.");
                break;

            case 4:
                config.Layout = "dedicated-roles";
                ConfigureDedicatedRoles(config);
                break;
        }

        Console.WriteLine($"  > Layout: {config.Layout}");
        Console.WriteLine();
    }

    private static void ConfigureWorktrees(QuadConfig config)
    {
        Console.WriteLine();
        Console.WriteLine("  Which repo should be the base?");

        // List repos in projects dir
        var repos = ListGitRepos(config.ProjectsDir);
        if (repos.Count > 0)
        {
            for (int i = 0; i < repos.Count; i++)
                Console.WriteLine($"    {i + 1}) {repos[i]}");
            Console.WriteLine($"    {repos.Count + 1}) Other (enter path)");
            Console.WriteLine();

            var choice = ReadChoice(1, repos.Count + 1, 1);
            config.WorktreeBase = choice <= repos.Count
                ? repos[choice - 1]
                : ReadLine("  Repo name or full path: ");
        }
        else
        {
            config.WorktreeBase = ReadLine("  Repo name or full path: ");
        }

        // Resolve full path
        var basePath = Path.IsPathRooted(config.WorktreeBase!)
            ? config.WorktreeBase
            : Path.Combine(config.ProjectsDir, config.WorktreeBase);

        if (!Directory.Exists(Path.Combine(basePath!, ".git")))
        {
            Console.WriteLine($"  Warning: '{basePath}' doesn't look like a git repo.");
            if (!Confirm("  Continue anyway?")) return;
        }

        // Base branch — the shared branch each worktree starts from and resyncs to on open.
        Console.WriteLine();
        Console.WriteLine("  Base branch: the up-to-date branch each worktree is cut from and");
        Console.WriteLine("  reset to every time its quad opens (e.g. main, develop, staging).");
        var bb = ReadLine($"  Base branch [{config.WorktreeBaseBranch}]: ");
        if (!string.IsNullOrWhiteSpace(bb)) config.WorktreeBaseBranch = bb.Trim();

        // Offer to create worktrees
        if (Confirm("  Create worktree directories now?"))
        {
            Console.WriteLine();
            for (int i = 1; i <= 4; i++)
            {
                var wtName = config.WorktreePattern.Replace("{base}", config.WorktreeBase!).Replace("{n}", i.ToString());
                var wtDir = Path.IsPathRooted(wtName) ? wtName : Path.Combine(config.ProjectsDir, wtName);
                var branch = $"quad-{i}";

                if (Directory.Exists(wtDir))
                {
                    Console.WriteLine($"  [OK] {Path.GetFileName(wtDir)}  (already exists)");
                    continue;
                }

                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "git",
                        Arguments = $"worktree add \"{wtDir}\" -b {branch} {config.WorktreeBaseBranch}",
                        WorkingDirectory = basePath,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false
                    };
                    var proc = Process.Start(psi)!;
                    proc.WaitForExit(30000);

                    if (proc.ExitCode == 0)
                        Console.WriteLine($"  [OK] {Path.GetFileName(wtDir)}  (branch: {branch})");
                    else
                    {
                        var err = proc.StandardError.ReadToEnd().Trim();
                        Console.WriteLine($"  [FAIL] {Path.GetFileName(wtDir)}  --{err}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  [FAIL] {Path.GetFileName(wtDir)}  --{ex.Message}");
                }
            }
        }

        config.QuadLabels = ["Quad 1", "Quad 2", "Quad 3", "Quad 4"];
    }

    private static void ConfigureDedicatedRoles(QuadConfig config)
    {
        Console.WriteLine();

        // Pick the project
        var repos = ListGitRepos(config.ProjectsDir);
        if (repos.Count > 0)
        {
            Console.WriteLine("  Which project?");
            for (int i = 0; i < repos.Count; i++)
                Console.WriteLine($"    {i + 1}) {repos[i]}");
            Console.WriteLine();
            var choice = ReadChoice(1, repos.Count, 1);
            config.DedicatedProject = repos[choice - 1];
        }
        else
        {
            config.DedicatedProject = ReadLine("  Project name or path: ");
        }

        // Name the roles
        Console.WriteLine();
        Console.WriteLine("  Name your quad roles (press Enter for defaults):");
        string[] defaults = ["Code", "Tests", "Review", "Research"];
        var labels = new string[4];
        for (int i = 0; i < 4; i++)
        {
            Console.Write($"    Quad {i + 1} [{defaults[i]}]: ");
            var input = Console.ReadLine()?.Trim();
            labels[i] = string.IsNullOrEmpty(input) ? defaults[i] : input;
        }
        config.QuadLabels = labels;
    }

    // ────────────────────────────────────────────────────────────
    //  Step 4: Sounds
    // ────────────────────────────────────────────────────────────

    private static void ConfigureSounds(QuadConfig config)
    {
        PrintStep(4, 7, "Sound Notifications");
        Console.WriteLine("  Play sounds when Claude finishes or needs attention?");
        Console.WriteLine("  (Requires WAV files in notification-pack/ folder)");
        Console.WriteLine();
        config.SoundsEnabled = Confirm("  Enable sounds?");
        Console.WriteLine($"  > Sounds: {(config.SoundsEnabled ? "Enabled" : "Disabled")}");
        Console.WriteLine();
    }

    // ────────────────────────────────────────────────────────────
    //  Step 5: Permission Mode
    // ────────────────────────────────────────────────────────────

    private static void ConfigurePermissionMode(QuadConfig config)
    {
        PrintStep(5, 7, "Claude Code Permission Mode");
        Console.WriteLine("  How should Claude Code handle tool permissions?");
        Console.WriteLine();
        Console.WriteLine("  1) Bypass Permissions  (recommended for power users)");
        Console.WriteLine("     Claude runs tools without asking. Fast but no guardrails.");
        Console.WriteLine();
        Console.WriteLine("  2) Auto-Accept");
        Console.WriteLine("     Claude auto-accepts most tools but still shows what it's doing.");
        Console.WriteLine();
        Console.WriteLine("  3) Manual  (default Claude behavior)");
        Console.WriteLine("     Claude asks before running each tool. Safest but slowest.");
        Console.WriteLine();

        var choice = ReadChoice(1, 3, 1);
        config.PermissionMode = choice switch
        {
            1 => "bypassPermissions",
            2 => "auto",
            _ => "manual"
        };

        Console.WriteLine($"  > Mode: {config.PermissionMode}");
        Console.WriteLine();
    }

    // ────────────────────────────────────────────────────────────
    //  Step 6: Allow-List
    // ────────────────────────────────────────────────────────────

    private static void ConfigureAllowList(QuadConfig config)
    {
        PrintStep(6, 7, "Permission Allow-List");

        if (config.PermissionMode == "bypassPermissions")
        {
            Console.WriteLine("  Bypass mode is active --allow-list is included as a safety net");
            Console.WriteLine("  in case you switch modes later.");
            Console.WriteLine();
        }
        else
        {
            Console.WriteLine("  Pre-approve common commands so Claude doesn't prompt for them.");
            Console.WriteLine();
        }

        Console.WriteLine("  Suggested allow-list:");
        Console.WriteLine("    [x] git commands (clone, status, diff, etc.)");
        Console.WriteLine("    [x] npm commands (install, run, etc.)");
        Console.WriteLine("    [x] prisma commands (generate, migrate)");
        Console.WriteLine("    [x] eslint / tsc");
        Console.WriteLine("    [x] dotenv utilities");
        Console.WriteLine("    [x] skill config commands");
        Console.WriteLine();

        if (!Confirm("  Apply suggested allow-list?"))
        {
            config.AllowList = [];
            Console.WriteLine("  > Allow-list: empty (you can edit settings.json later)");
        }
        else
        {
            Console.WriteLine("  > Allow-list: applied");
        }

        Console.WriteLine();
    }

    // ────────────────────────────────────────────────────────────
    //  Step 7: Review & Apply
    // ────────────────────────────────────────────────────────────

    private static int ReviewAndApply(QuadConfig config)
    {
        Console.WriteLine("=============================================");
        Console.WriteLine("  Configuration Summary");
        Console.WriteLine("=============================================");
        Console.WriteLine($"  Shell:         {config.ShellType} ({config.ShellExe})");
        Console.WriteLine($"  Terminal:      {config.TerminalProfile}");
        Console.WriteLine($"  Projects dir:  {config.ProjectsDir}");
        Console.WriteLine($"  Layout:        {config.Layout}");
        if (config.WorktreeBase != null)
            Console.WriteLine($"  Worktree base: {config.WorktreeBase}");
        if (config.DedicatedProject != null)
            Console.WriteLine($"  Project:       {config.DedicatedProject}");
        Console.WriteLine($"  Quad labels:   {string.Join(", ", config.QuadLabels)}");
        Console.WriteLine($"  Sounds:        {(config.SoundsEnabled ? "Enabled" : "Disabled")}");
        Console.WriteLine($"  Permissions:   {config.PermissionMode}");
        Console.WriteLine($"  Allow-list:    {config.AllowList.Length} rules");
        Console.WriteLine("=============================================");
        Console.WriteLine();

        if (!Confirm("  Apply this configuration?"))
        {
            Console.WriteLine("  Setup cancelled.");
            return 0;
        }

        Console.WriteLine();

        // 1. Save config
        config.Save();
        Console.WriteLine($"  [OK] Config saved to {QuadConfig.ConfigPath}");

        // 2. Update claude-launch.sh
        UpdateLaunchScript(config);

        // 3. Write launch-env.sh
        WriteLaunchEnv(config);

        // 4. Merge settings.json
        MergeClaudeSettings(config);

        // 5. Offer to install the bundled helper slash-commands.
        InstallHelperCommands(config);

        Console.WriteLine();
        Console.WriteLine("  You're ready! Run: QuadClaude.exe launch");
        Console.WriteLine();

        return 0;
    }

    // ────────────────────────────────────────────────────────────
    //  Apply Helpers
    // ────────────────────────────────────────────────────────────

    // Copy the bundled slash-commands (.claude/commands/*.md in the repo) into
    // the user's ~/.claude/commands so they work in EVERY Claude Code session on
    // this machine — including work worktrees, which are separate repos. Opt-in,
    // copy-if-missing (never clobbers a same-named command the user already has),
    // and trivially reversible (they're plain files).
    private static void InstallHelperCommands(QuadConfig config)
    {
        var srcDir = Path.Combine(config.SetupDir, ".claude", "commands");
        if (!Directory.Exists(srcDir)) return;
        var files = Directory.GetFiles(srcDir, "*.md");
        if (files.Length == 0) return;

        var destDir = Path.Combine(PathHelper.HomeDir, ".claude", "commands");

        Console.WriteLine();
        Console.WriteLine("  Helper commands");
        Console.WriteLine("  QuadClaude bundles a few optional slash-commands:");
        Console.WriteLine("    /explain-quad-claude  /review-pr  /commit-and-pr  /sync-base  /new-worktree");
        Console.WriteLine("  Installing them copies plain markdown files into:");
        Console.WriteLine($"    {destDir}");
        Console.WriteLine("  That makes them available in EVERY Claude Code session on this machine");
        Console.WriteLine("  (including your work worktrees). This is opt-in — say no to skip, and you");
        Console.WriteLine("  can remove any later by deleting its .md file from that folder.");
        Console.WriteLine();

        if (!Confirm("  Install these helper commands now?"))
        {
            Console.WriteLine("  [SKIP] Helper commands not installed.");
            return;
        }

        Directory.CreateDirectory(destDir);
        foreach (var f in files)
        {
            var name = Path.GetFileName(f);
            var dest = Path.Combine(destDir, name);
            if (File.Exists(dest))
            {
                Console.WriteLine($"  [SKIP] {name} — you already have a command by this name (left it alone).");
                continue;
            }
            try
            {
                File.Copy(f, dest);
                Console.WriteLine($"  [OK] {name}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  [FAIL] {name} — {ex.Message}");
            }
        }
    }

    private static void UpdateLaunchScript(QuadConfig config)
    {
        var scriptPath = Path.Combine(config.SetupDir, "claude-launch.sh");
        if (!File.Exists(scriptPath))
        {
            Console.WriteLine($"  [WARN] claude-launch.sh not found at {scriptPath}");
            return;
        }

        var msysProjectsDir = PathHelper.ToMsysPath(config.ProjectsDir);
        var lines = File.ReadAllLines(scriptPath);
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].TrimStart().StartsWith("PROJECTS_DIR="))
            {
                lines[i] = $"PROJECTS_DIR=\"${{QUADCLAUDE_PROJECTS_DIR:-{msysProjectsDir}}}\"";
                break;
            }
        }
        File.WriteAllLines(scriptPath, lines);
        Console.WriteLine("  [OK] Updated claude-launch.sh");
    }

    private static void WriteLaunchEnv(QuadConfig config)
    {
        var appDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "QuadClaude");
        Directory.CreateDirectory(appDataDir);

        var envPath = Path.Combine(appDataDir, "launch-env.sh");
        var msysProjectsDir = PathHelper.ToMsysPath(config.ProjectsDir);

        var lines = new List<string>
        {
            "# Generated by QuadClaude setup --do not edit manually",
            $"QUADCLAUDE_PROJECTS_DIR=\"{msysProjectsDir}\"",
            $"QUADCLAUDE_LAYOUT=\"{config.Layout}\""
        };

        if (config.WorktreeBase != null)
        {
            lines.Add($"QUADCLAUDE_WORKTREE_BASE=\"{config.WorktreeBase}\"");
            lines.Add($"QUADCLAUDE_WORKTREE_PATTERN=\"{config.WorktreePattern}\"");
            lines.Add($"QUADCLAUDE_WORKTREE_BASE_BRANCH=\"{config.WorktreeBaseBranch}\"");
        }

        if (!string.IsNullOrWhiteSpace(config.DevServerSubdir))
            lines.Add($"QUADCLAUDE_WORKTREE_SUBDIR=\"{config.DevServerSubdir}\"");

        if (!string.IsNullOrWhiteSpace(config.WorktreeProvisionCommand))
            lines.Add($"QUADCLAUDE_PROVISION_CMD=\"{config.WorktreeProvisionCommand}\"");

        if (config.DedicatedProject != null)
        {
            var targetDir = Path.IsPathRooted(config.DedicatedProject)
                ? config.DedicatedProject
                : Path.Combine(config.ProjectsDir, config.DedicatedProject);
            lines.Add($"QUADCLAUDE_TARGET_DIR=\"{PathHelper.ToMsysPath(targetDir)}\"");
        }

        // Quad labels as a bash array
        var escaped = config.QuadLabels.Select(l => $"\"{l}\"");
        lines.Add($"QUADCLAUDE_LABELS=({string.Join(" ", escaped)})");

        File.WriteAllLines(envPath, lines);
        Console.WriteLine("  [OK] Wrote launch-env.sh");
    }

    private static void MergeClaudeSettings(QuadConfig config)
    {
        var settingsPath = PathHelper.ClaudeSettingsPath;
        JsonNode root;

        if (File.Exists(settingsPath))
        {
            try
            {
                root = JsonNode.Parse(File.ReadAllText(settingsPath)) ?? new JsonObject();
            }
            catch
            {
                Console.WriteLine("  [WARN] Existing settings.json is invalid JSON --creating fresh");
                root = new JsonObject();
            }
        }
        else
        {
            Directory.CreateDirectory(PathHelper.ClaudeDir);
            root = new JsonObject();
        }

        var obj = root.AsObject();

        // Permissions
        if (config.PermissionMode != "manual")
        {
            var perms = obj["permissions"]?.AsObject() ?? new JsonObject();
            perms["defaultMode"] = config.PermissionMode;

            if (config.AllowList.Length > 0)
            {
                var allowArray = new JsonArray();
                foreach (var rule in config.AllowList)
                    allowArray.Add(rule);
                perms["allow"] = allowArray;
            }

            // additionalDirectories --ensure .claude is included
            var claudeDir = PathHelper.ClaudeDir.Replace("/", "\\");
            var addDirs = perms["additionalDirectories"]?.AsArray() ?? new JsonArray();
            bool hasClaudeDir = false;
            foreach (var d in addDirs)
            {
                if (d?.ToString().Equals(claudeDir, StringComparison.OrdinalIgnoreCase) == true)
                {
                    hasClaudeDir = true;
                    break;
                }
            }
            if (!hasClaudeDir)
            {
                addDirs.Add(claudeDir);
            }
            // Detach from old parent before assigning
            if (perms["additionalDirectories"] != null)
                perms["additionalDirectories"] = null;
            perms["additionalDirectories"] = addDirs;

            if (obj["permissions"] != null)
                obj["permissions"] = null;
            obj["permissions"] = perms;
        }

        // Bypass mode prompt suppression
        if (config.PermissionMode == "bypassPermissions")
            obj["skipDangerousModePermissionPrompt"] = true;

        // Hooks
        var setupDirEscaped = PathHelper.ToJsonEscaped(config.SetupDir);
        var hooks = obj["hooks"]?.AsObject() ?? new JsonObject();

        // Build hook commands
        var quadExePath = Path.Combine(config.SetupDir, "QuadClaude", "publish", "QuadClaude.exe");
        var quadExeEscaped = PathHelper.ToJsonEscaped(quadExePath);

        // Stop hooks: sound + green glow
        var stopHooks = new JsonArray();
        var stopInner = new JsonArray();

        if (config.SoundsEnabled)
        {
            var soundPath = Path.Combine(config.SetupDir, "notification-pack", "Email-Notification-Inbox-Snap.wav");
            var soundEscaped = PathHelper.ToJsonEscaped(soundPath);
            stopInner.Add(MakeHook($"powershell -ExecutionPolicy Bypass -NonInteractive -Command \"(New-Object System.Media.SoundPlayer '{soundEscaped}').PlaySync()\""));
        }
        stopInner.Add(MakeHook($"\"{quadExeEscaped}\" glow --color green"));

        // Track cwd/branch after each Claude response (native exe, no bash needed)
        stopInner.Add(MakeHook($"\"{quadExeEscaped}\" track"));

        // Record the session as finished so the idle overlay flips to "Done".
        stopInner.Add(MakeHook($"\"{quadExeEscaped}\" session-state done"));

        stopHooks.Add(new JsonObject { ["hooks"] = stopInner });

        // UserPromptSubmit: yellow glow (= Claude is working) + track. GlowCommand kills
        // any existing glow (red/teal/green) before showing yellow, so this also clears
        // stale alert glows from the previous turn.
        var promptHooks = new JsonArray();
        var promptInner = new JsonArray();
        promptInner.Add(MakeHook($"\"{quadExeEscaped}\" glow --color yellow"));
        promptInner.Add(MakeHook($"\"{quadExeEscaped}\" track"));
        // Mark the session busy so the idle overlay shows "Working".
        promptInner.Add(MakeHook($"\"{quadExeEscaped}\" session-state busy"));
        promptHooks.Add(new JsonObject { ["hooks"] = promptInner });

        // Notification hooks: permission_prompt (sound only), idle_prompt (sound only)
        var notifHooks = new JsonArray();
        var notifSoundPath = Path.Combine(config.SetupDir, "notification-pack", "mixkit-dry-pop-up-notification-alert-2356.wav");
        var notifSoundEscaped = PathHelper.ToJsonEscaped(notifSoundPath);

        // permission_prompt — sound only, no glow (avoids permanent red)
        var permInner = new JsonArray();
        if (config.SoundsEnabled)
            permInner.Add(MakeHook($"powershell -ExecutionPolicy Bypass -NonInteractive -Command \"(New-Object System.Media.SoundPlayer '{notifSoundEscaped}').PlaySync()\""));
        // Paused on a permission prompt → overlay shows "Needs Input".
        permInner.Add(MakeHook($"\"{quadExeEscaped}\" session-state needs-input"));
        notifHooks.Add(new JsonObject
        {
            ["matcher"] = "permission_prompt",
            ["hooks"] = permInner
        });

        // idle_prompt — sound only
        var idleInner = new JsonArray();
        if (config.SoundsEnabled)
            idleInner.Add(MakeHook($"powershell -ExecutionPolicy Bypass -NonInteractive -Command \"(New-Object System.Media.SoundPlayer '{notifSoundEscaped}').PlaySync()\""));
        notifHooks.Add(new JsonObject
        {
            ["matcher"] = "idle_prompt",
            ["hooks"] = idleInner
        });

        // PreToolUse: kill dev server before build/lint/push
        var preToolHooks = new JsonArray();
        preToolHooks.Add(new JsonObject
        {
            ["matcher"] = "Bash",
            ["hooks"] = new JsonArray { MakeHook($"\"{quadExeEscaped}\" kill-server-if-build", async_: false) }
        });
        // Any tool running means the session is active — re-assert "busy" so the overlay
        // recovers from a stale "needs-input"/"done" once work resumes.
        preToolHooks.Add(new JsonObject
        {
            ["hooks"] = new JsonArray { MakeHook($"\"{quadExeEscaped}\" session-state busy") }
        });

        // PostToolUse: track on Bash
        var postToolHooks = new JsonArray();
        postToolHooks.Add(new JsonObject
        {
            ["matcher"] = "Bash",
            ["hooks"] = new JsonArray { MakeHook($"\"{quadExeEscaped}\" track") }
        });

        // Assign hooks (replace existing QuadClaude hooks)
        if (obj["hooks"] != null) obj["hooks"] = null;
        hooks = new JsonObject
        {
            ["Stop"] = stopHooks,
            ["UserPromptSubmit"] = promptHooks,
            ["PreToolUse"] = preToolHooks,
            ["PostToolUse"] = postToolHooks,
            ["Notification"] = notifHooks
        };
        obj["hooks"] = hooks;

        // Write
        var options = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(settingsPath, obj.ToJsonString(options));
        Console.WriteLine($"  [OK] Wrote {settingsPath}");
    }

    private static JsonObject MakeHook(string command, bool async_ = true)
    {
        return new JsonObject
        {
            ["type"] = "command",
            ["command"] = command,
            ["async"] = async_
        };
    }

    // ────────────────────────────────────────────────────────────
    //  Detection Helpers
    // ────────────────────────────────────────────────────────────

    private static string? DetectGitBash()
    {
        // Preferred locations --bin\bash.exe is the standard Git for Windows entry point.
        // usr\bin\bash.exe also works but bin\bash.exe sets up the Git environment properly.
        string[] preferred =
        [
            @"C:\Program Files\Git\bin\bash.exe",
            @"C:\Program Files (x86)\Git\bin\bash.exe"
        ];

        foreach (var path in preferred)
        {
            if (File.Exists(path)) return path;
        }

        // Check common per-user install locations
        var localPrograms = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs");
        string[] userPaths =
        [
            Path.Combine(localPrograms, "Git", "bin", "bash.exe"),
            Path.Combine(localPrograms, "Git", "usr", "bin", "bash.exe")
        ];

        foreach (var path in userPaths)
        {
            if (File.Exists(path)) return path;
        }

        // Scan all fixed drives for Git installations
        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (drive.DriveType != DriveType.Fixed || !drive.IsReady) continue;
                var root = drive.RootDirectory.FullName;

                string[] drivePaths =
                [
                    Path.Combine(root, "Program Files", "Git", "bin", "bash.exe"),
                    Path.Combine(root, "Program Files (x86)", "Git", "bin", "bash.exe"),
                    Path.Combine(root, "Git", "bin", "bash.exe")
                ];

                foreach (var path in drivePaths)
                {
                    if (File.Exists(path)) return path;
                }
            }
        }
        catch { }

        // Fall back to PATH lookup via `where`
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "where",
                Arguments = "bash.exe",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            var proc = Process.Start(psi)!;
            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(5000);

            string? bestMatch = null;
            foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = line.Trim();
                if (!trimmed.Contains("Git", StringComparison.OrdinalIgnoreCase)) continue;

                // Prefer bin\bash.exe over usr\bin\bash.exe
                if (trimmed.Contains(@"\bin\bash.exe", StringComparison.OrdinalIgnoreCase)
                    && !trimmed.Contains(@"\usr\bin\", StringComparison.OrdinalIgnoreCase))
                    return trimmed;

                bestMatch ??= trimmed;
            }

            if (bestMatch != null) return bestMatch;
        }
        catch { }

        return null;
    }

    private static List<string> DetectProjectsDirs()
    {
        var home = PathHelper.HomeDir;
        var candidates = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Common locations under the user's home directory
        string[] homePatterns =
        [
            "Projects",
            "repos",
            "Repos",
            "dev",
            "Dev",
            "source/repos",
            "Documents/Projects",
            "Code",
            "code",
            "src"
        ];

        foreach (var rel in homePatterns)
        {
            var path = Path.Combine(home, rel);
            if (Directory.Exists(path) && seen.Add(Path.GetFullPath(path)))
                candidates.Add(Path.GetFullPath(path));
        }

        // Scan drive roots for common top-level project directories
        // (e.g., C:\Projects, D:\repos, etc.)
        string[] rootPatterns = ["Projects", "repos", "Repos", "Dev", "dev", "Code", "src"];

        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (drive.DriveType is not (DriveType.Fixed or DriveType.Network)) continue;
                if (!drive.IsReady) continue;

                foreach (var rel in rootPatterns)
                {
                    var path = Path.Combine(drive.RootDirectory.FullName, rel);
                    if (Directory.Exists(path) && seen.Add(Path.GetFullPath(path)))
                        candidates.Add(Path.GetFullPath(path));
                }
            }
        }
        catch { /* drive enumeration can fail in restricted environments */ }

        return candidates;
    }

    private static int CountGitRepos(string dir)
    {
        try
        {
            return Directory.GetDirectories(dir)
                .Count(sub => Directory.Exists(Path.Combine(sub, ".git")));
        }
        catch { return 0; }
    }

    private static List<string> ListGitRepos(string dir)
    {
        var repos = new List<string>();
        if (!Directory.Exists(dir)) return repos;

        foreach (var sub in Directory.GetDirectories(dir))
        {
            if (Directory.Exists(Path.Combine(sub, ".git")))
                repos.Add(Path.GetFileName(sub));
        }

        return repos;
    }

    private static string FindSetupDir()
    {
        // Walk up from the exe to find the QuadClaude repo root
        var exeDir = AppContext.BaseDirectory;
        var dir = new DirectoryInfo(exeDir);

        while (dir != null)
        {
            // Look for claude-launch.sh as a marker
            if (File.Exists(Path.Combine(dir.FullName, "claude-launch.sh")))
                return dir.FullName;
            dir = dir.Parent;
        }

        // Fallback: assume standard structure (exe is in QuadClaude/publish/)
        var fallback = Path.GetFullPath(Path.Combine(exeDir, "..", ".."));
        return Directory.Exists(fallback) ? fallback : exeDir;
    }

    // ────────────────────────────────────────────────────────────
    //  UI Helpers
    // ────────────────────────────────────────────────────────────

    private static void PrintHeader()
    {
        // Force UTF-8 output if possible
        try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch { }

        Console.WriteLine();
        Console.WriteLine("+--------------------------------------------+");
        Console.WriteLine("|        QuadClaude Setup Wizard              |");
        Console.WriteLine("+--------------------------------------------+");
        Console.WriteLine();
    }

    private static void PrintStep(int step, int total, string title)
    {
        Console.WriteLine($"[{step}/{total}] {title}");
    }

    private static int ReadChoice(int min, int max, int defaultChoice)
    {
        while (true)
        {
            Console.Write($"  Choose [{min}-{max}] (default: {defaultChoice}): ");
            var input = Console.ReadLine()?.Trim();
            if (string.IsNullOrEmpty(input)) return defaultChoice;
            if (int.TryParse(input, out int val) && val >= min && val <= max)
                return val;
            Console.WriteLine($"  Please enter a number between {min} and {max}.");
        }
    }

    private static bool Confirm(string prompt, bool defaultYes = true)
    {
        var hint = defaultYes ? "[Y/n]" : "[y/N]";
        Console.Write($"{prompt} {hint}: ");
        var input = Console.ReadLine()?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(input)) return defaultYes;
        return input.StartsWith('y');
    }

    private static string ReadPath(string prompt)
    {
        int attempts = 0;
        while (true)
        {
            Console.Write(prompt);
            var input = Console.ReadLine();

            // EOF --no interactive console available
            if (input == null)
            {
                Console.WriteLine();
                Console.WriteLine("  Error: no interactive input available. Run this command from a terminal.");
                Environment.Exit(1);
            }

            var trimmed = input.Trim().Trim('"');
            if (!string.IsNullOrEmpty(trimmed))
                return trimmed;

            attempts++;
            if (attempts >= 5)
            {
                Console.WriteLine("  Too many empty inputs. Exiting setup.");
                Environment.Exit(1);
            }

            Console.WriteLine("  Please enter a path.");
        }
    }

    private static string ReadLine(string prompt)
    {
        Console.Write(prompt);
        return Console.ReadLine()?.Trim() ?? "";
    }
}
