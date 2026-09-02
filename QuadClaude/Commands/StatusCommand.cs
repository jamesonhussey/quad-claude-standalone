using System.Windows;
using QuadClaude.Interop;
using QuadClaude.Overlay;

namespace QuadClaude.Commands;

public static class StatusCommand
{
    /// <summary>
    /// Launches a standalone StatusWidget attached to a terminal window.
    /// Can be called with explicit --hwnd/--quad (from LaunchCommand) or
    /// auto-detect via foreground window (manual invocation from terminal).
    /// </summary>
    public static int Execute(long hwnd, int quadIndex)
    {
        IntPtr hWnd;
        if (hwnd > 0)
        {
            hWnd = new IntPtr(hwnd);
        }
        else
        {
            // Auto-detect: find the terminal we're running in
            var (found, _) = ProcessTreeWalker.FindParentTerminalWindow();
            if (found == IntPtr.Zero) return 1;
            hWnd = found;
        }

        if (!NativeMethods.IsWindow(hWnd)) return 1;

        var app = new Application();
        app.Run(new StatusWidget(hWnd, quadIndex));
        return 0;
    }
}
