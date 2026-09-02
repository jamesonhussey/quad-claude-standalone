using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using QuadClaude.Config;
using QuadClaude.Interop;
using QuadClaude.Overlay;

namespace QuadClaude.Commands;

public static class GlowCommand
{
    public static string PhaseToGlowColor(string? phase) => phase switch
    {
        "Working" => "yellow",
        _         => "green",
    };

    private static string ResolveFromConfig(string color)
    {
        try
        {
            var config = QuadConfig.Load();
            if (config == null) return color;
            return color switch
            {
                "yellow" => config.GlowColorWorking,
                "green"  => config.GlowColorDone,
                _        => color,
            };
        }
        catch { return color; }
    }

    public static int Execute(string color, bool fromStatus = false)
    {
        IntPtr hWnd;
        string pidKey;

        // Prefer stored quad mapping (set by LaunchCommand, inherited via QUAD_INDEX env var)
        var quadIndex = Environment.GetEnvironmentVariable("QUAD_INDEX");

        if (fromStatus)
            color = ResolveColorFromStatus(quadIndex);
        color = ResolveFromConfig(color);

        if (color == "none") return 0;

        if (quadIndex != null)
        {
            var dir = QuadClaude.Config.PathHelper.AppDataDir;
            var hwndFile = Path.Combine(dir, $"quad-{quadIndex}.hwnd");
            if (File.Exists(hwndFile)
                && long.TryParse(File.ReadAllText(hwndFile).Trim(), out long storedHwnd)
                && NativeMethods.IsWindow(new IntPtr(storedHwnd)))
            {
                hWnd = new IntPtr(storedHwnd);
                pidKey = $"quad-{quadIndex}";
            }
            else
            {
                // Stored HWND stale — fall back to tree walk
                var (found, terminalPid) = ProcessTreeWalker.FindParentTerminalWindow();
                if (found == IntPtr.Zero) return 1;
                hWnd = found;
                pidKey = terminalPid > 0 ? terminalPid.ToString() : "default";
            }
        }
        else
        {
            // Manual invocation — use tree walk
            var (found, terminalPid) = ProcessTreeWalker.FindParentTerminalWindow();
            if (found == IntPtr.Zero) return 1;
            hWnd = found;
            pidKey = terminalPid > 0 ? terminalPid.ToString() : "default";
        }

        // Build PID file path (per-quad or per-terminal)
        var pidFile = Path.Combine(Path.GetTempPath(), $"glow-border-{pidKey}.pid");

        // Kill any existing glow for this terminal
        KillExistingGlow(pidFile);

        // Write our PID
        File.WriteAllText(pidFile, Environment.ProcessId.ToString());

        // Show the overlay
        var app = new Application();
        app.Run(new GlowWindow(hWnd, color));

        // Cleanup
        try { File.Delete(pidFile); } catch { }
        return 0;
    }

    private static void KillExistingGlow(string pidFile)
    {
        if (!File.Exists(pidFile)) return;
        try
        {
            var text = File.ReadAllText(pidFile).Trim();
            if (int.TryParse(text, out int pid))
                Process.GetProcessById(pid).Kill();
        }
        catch { }
        try { File.Delete(pidFile); } catch { }
    }

    private static string ResolveColorFromStatus(string? quadIndex)
    {
        if (quadIndex == null) return "green";

        var statusFile = Path.Combine(
            QuadClaude.Config.PathHelper.AppDataDir, $"status-quad-{quadIndex}.json");

        try
        {
            if (!File.Exists(statusFile)) return "green";
            var json = File.ReadAllText(statusFile);
            using var doc = JsonDocument.Parse(json);
            var phase = doc.RootElement.TryGetProperty("Phase", out var p) ? p.GetString() : null;
            return PhaseToGlowColor(phase);
        }
        catch
        {
            return "green";
        }
    }
}
