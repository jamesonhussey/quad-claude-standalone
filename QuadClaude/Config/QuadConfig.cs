using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace QuadClaude.Config;

public class QuadConfig
{
    public string ProjectsDir { get; set; } = "";
    public string SetupDir { get; set; } = "";
    public string ShellExe { get; set; } = "";
    public string ShellType { get; set; } = "gitbash"; // gitbash, wsl, powershell, other
    public string TerminalProfile { get; set; } = "Git Bash";
    public string Layout { get; set; } = "multi-project"; // multi-project, worktrees, hybrid, dedicated-roles
    public bool SoundsEnabled { get; set; } = true;
    public string[] QuadLabels { get; set; } = ["Quad 1", "Quad 2", "Quad 3", "Quad 4"];
    public string? WorktreeBase { get; set; }
    public string WorktreePattern { get; set; } = "{base} - Quad-{n}";
    /// <summary>Branch each worktree is synced/detached to when its quad opens (e.g. "main", "develop", "staging").</summary>
    public string WorktreeBaseBranch { get; set; } = "main";
    /// <summary>Optional command run once inside a freshly-cut worktree after env-copy + npm install
    /// (e.g. "npx prisma generate"). Runs from the worktree root. Leave null to skip.</summary>
    public string? WorktreeProvisionCommand { get; set; }
    public string? DedicatedProject { get; set; }
    public string TargetMonitor { get; set; } = "largest";
    public string WindowLayout { get; set; } = "grid";
    // Carousel focus mode: when on (focus layout only), the quad in the big focus pane
    // is auto-demoted the moment it starts working and an idle quad is pulled up in its
    // place — so working sessions self-sort to the small panes. Off by default.
    public bool CarouselModeEnabled { get; set; } = false;
    public int[] DevServerPorts { get; set; } = [3000, 3001, 3002, 3003];
    public string DevServerCommand { get; set; } = "npm run dev -- --port {port}";
    /// <summary>Absolute path override for the dev server tab cwd. If set, used as-is (ignores tracked cwd).</summary>
    public string? DevServerCwd { get; set; }
    /// <summary>Subdirectory appended to the resolved cwd (e.g. "apps/main-app" for monorepos). Optional.</summary>
    public string? DevServerSubdir { get; set; }
    public string GlowColorWorking { get; set; } = "#FFB347";
    public string GlowColorDone { get; set; } = "#00FF88";
    // Shown when a session is paused on a permission/needs-input prompt. Matches
    // the red permission glow border.
    public string GlowColorNeedsInput { get; set; } = "#FF4D4D";
    public bool IdleOverlayEnabled { get; set; } = true;
    public int IdleOverlayDelaySeconds { get; set; } = 30;
    public bool PartyModeEnabled { get; set; } = false;
    public bool IdleTintEnabled { get; set; } = true;
    public double IdleTintAmount { get; set; } = 0.25;
    public string PermissionMode { get; set; } = "bypassPermissions"; // bypassPermissions, auto, manual
    public string[] AllowList { get; set; } =
    [
        "Bash(git clone:*)",
        "Bash(npm install:*)",
        "Bash(npm run:*)",
        "Bash(npx prisma:*)",
        "Bash(npx eslint:*)",
        "Bash(node_modules/.bin/prisma generate:*)",
        "Bash(node_modules/.bin/tsc --noEmit)",
        "Bash(npx dotenv-cli:*)",
        "Bash(npx dotenv:*)",
        "Bash(node -e \":*)",
        "Skill(update-config)",
        "Skill(update-config:*)"
    ];

    // ── Monday.com integration (optional, off by default) ────────
    // Master switch for the Monday panel + its status-widget button. Off by
    // default so the overlay ships with no Monday surface at all. Flip to true
    // (and fill in the fields below) only if you use Monday.com.
    public bool MondayEnabled { get; set; } = false;
    // All fields blank by default — Monday is off until you configure it.
    // Personal API token (Monday → Developer → My Access Tokens). Read from
    // config.json or the MONDAY_API_TOKEN env var — never hardcoded.
    public string? MondayApiToken { get; set; }
    // Board to pull action items from.
    public long MondayBoardId { get; set; } = 0;
    // Group within the board to cycle through.
    public string MondayGroupId { get; set; } = "";
    // monday account subdomain, used to build item URLs (e.g. "yourco.monday.com").
    public string MondayHost { get; set; } = "";
    // monday user id used to filter "my" items.
    public string? MondayUserId { get; set; }
    // Which fetch backend the panel uses by default: "graphql" or "cli".
    public string MondayTaskSource { get; set; } = "graphql";

    private static string AppDataDir => PathHelper.AppDataDir;

    public static string ConfigPath => Path.Combine(AppDataDir, "config.json");

    public static bool Exists => File.Exists(ConfigPath);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static QuadConfig? Load()
    {
        var path = ConfigPath;
        if (!File.Exists(path))
        {
            // Dev/alt instance with no config of its own → inherit the base
            // instance's config (so it gets projectsDir, token, etc. for free).
            var basePath = Path.Combine(PathHelper.BaseAppDataDir, "config.json");
            if (!File.Exists(basePath)) return null;
            path = basePath;
        }
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<QuadConfig>(json, JsonOptions);
    }

    public static QuadConfig LoadOrThrow()
    {
        return Load() ?? throw new InvalidOperationException(
            "QuadClaude is not configured. Run: QuadClaude.exe setup");
    }

    public void Save()
    {
        Directory.CreateDirectory(AppDataDir);
        var json = JsonSerializer.Serialize(this, JsonOptions);
        File.WriteAllText(ConfigPath, json);
    }
}
