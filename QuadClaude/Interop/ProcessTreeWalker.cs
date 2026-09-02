using System.Diagnostics;

namespace QuadClaude.Interop;

public static class ProcessTreeWalker
{
    private static readonly string[] TerminalProcessNames =
    [
        "WindowsTerminal",
        "warp",
    ];

    /// <summary>
    /// Finds the parent terminal window for the current process.
    /// Strategy 1: Check if the foreground window is a terminal (best for multi-window WT).
    /// Strategy 2: Walk the process tree, then pick the foreground WT window if multiple exist.
    /// </summary>
    public static (IntPtr hWnd, int terminalPid) FindParentTerminalWindow()
    {
        // Strategy 1 (preferred): walk up process tree to find our actual parent terminal.
        // This is reliable even for async hooks where another terminal may be focused.
        int currentPid = Environment.ProcessId;
        for (int i = 0; i < 15; i++)
        {
            int parentPid = GetParentPid(currentPid);
            if (parentPid <= 0) break;

            try
            {
                var proc = Process.GetProcessById(parentPid);
                if (TerminalProcessNames.Any(n => proc.ProcessName.Equals(n, StringComparison.OrdinalIgnoreCase)))
                {
                    var hWnd = FindBestWindowForPid((uint)parentPid);
                    if (hWnd != IntPtr.Zero)
                        return (hWnd, parentPid);
                }
            }
            catch { /* process gone */ }

            currentPid = parentPid;
        }

        // Strategy 2 (fallback): check if the foreground window is a terminal.
        // Used when process tree walk fails (e.g. intermediate process exited).
        var fgHwnd = NativeMethods.GetForegroundWindow();
        if (fgHwnd != IntPtr.Zero)
        {
            NativeMethods.GetWindowThreadProcessId(fgHwnd, out uint fgPid);
            try
            {
                var proc = Process.GetProcessById((int)fgPid);
                if (TerminalProcessNames.Any(n => proc.ProcessName.Equals(n, StringComparison.OrdinalIgnoreCase)))
                    return (fgHwnd, (int)fgPid);
            }
            catch { }
        }

        return (IntPtr.Zero, 0);
    }

    /// <summary>
    /// Enumerates all visible windows for a PID and picks the best one.
    /// Prefers the foreground window when multiple exist (multi-window WT).
    /// </summary>
    private static IntPtr FindBestWindowForPid(uint targetPid)
    {
        var windows = new List<IntPtr>();
        NativeMethods.EnumWindows((hWnd, _) =>
        {
            if (!NativeMethods.IsWindowVisible(hWnd)) return true;
            NativeMethods.GetWindowThreadProcessId(hWnd, out uint pid);
            if (pid == targetPid)
                windows.Add(hWnd);
            return true;
        }, IntPtr.Zero);

        if (windows.Count == 0) return IntPtr.Zero;
        if (windows.Count == 1) return windows[0];

        // Multiple windows (multi-window WT) — prefer the foreground one
        var fg = NativeMethods.GetForegroundWindow();
        if (windows.Contains(fg)) return fg;

        return windows[0];
    }

    private static int GetParentPid(int pid)
    {
        try
        {
            var handle = Process.GetProcessById(pid).Handle;
            var pbi = new PROCESS_BASIC_INFORMATION();
            int returnLength;
            int status = NativeMethods.NtQueryInformationProcess(
                handle, 0, ref pbi, System.Runtime.InteropServices.Marshal.SizeOf(pbi), out returnLength);

            if (status == 0)
                return pbi.InheritedFromUniqueProcessId.ToInt32();
        }
        catch { }
        return -1;
    }
}
