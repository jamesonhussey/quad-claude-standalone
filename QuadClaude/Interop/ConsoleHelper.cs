using System.IO;
using System.Runtime.InteropServices;

namespace QuadClaude.Interop;

public static class ConsoleHelper
{
    private const int ATTACH_PARENT_PROCESS = -1;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AllocConsole();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FreeConsole();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int nStdHandle);

    private const int STD_OUTPUT_HANDLE = -11;
    private const int STD_INPUT_HANDLE = -10;

    private static bool _attached;

    /// <summary>
    /// Attach to the parent console (if launched from a terminal) or allocate a new one.
    /// Call this before any Console.ReadLine/WriteLine in the setup wizard.
    /// </summary>
    public static void AttachOrAlloc()
    {
        if (_attached) return;

        // Try to attach to the parent process's console (e.g., Git Bash, cmd, PowerShell)
        if (!AttachConsole(ATTACH_PARENT_PROCESS))
        {
            // No parent console — allocate our own (e.g., double-clicked the exe)
            AllocConsole();
        }

        // Redirect Console streams to the attached/allocated console
        var stdOut = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
        var stdIn = new StreamReader(Console.OpenStandardInput());
        Console.SetOut(stdOut);
        Console.SetIn(stdIn);

        _attached = true;
    }
}
