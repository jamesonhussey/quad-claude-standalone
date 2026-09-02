using System.Diagnostics;
using System.IO;
using QuadClaude.Interop;

namespace QuadClaude.Commands;

public static class KillGlowCommand
{
    public static int Execute()
    {
        // Try quad-specific PID file first (from QUAD_INDEX env var)
        var quadIndex = Environment.GetEnvironmentVariable("QUAD_INDEX");
        if (quadIndex != null)
        {
            var pidFile = Path.Combine(Path.GetTempPath(), $"glow-border-quad-{quadIndex}.pid");
            if (KillFromPidFile(pidFile)) return 0;
        }

        // Try per-terminal PID file
        var (_, terminalPid) = ProcessTreeWalker.FindParentTerminalWindow();
        if (terminalPid > 0)
        {
            var pidFile = Path.Combine(Path.GetTempPath(), $"glow-border-{terminalPid}.pid");
            if (KillFromPidFile(pidFile)) return 0;
        }

        // Fallback: try the generic PID file
        var fallbackFile = Path.Combine(Path.GetTempPath(), "glow-border.pid");
        KillFromPidFile(fallbackFile);
        return 0;
    }

    private static bool KillFromPidFile(string pidFile)
    {
        if (!File.Exists(pidFile)) return false;
        try
        {
            var text = File.ReadAllText(pidFile).Trim();
            if (int.TryParse(text, out int pid))
                Process.GetProcessById(pid).Kill();
        }
        catch { }
        try { File.Delete(pidFile); } catch { }
        return true;
    }
}
