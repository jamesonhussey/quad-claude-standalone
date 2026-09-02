using QuadClaude.Commands;
using QuadClaude.Interop;

namespace QuadClaude;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return 1;
        }

        // Setup needs console I/O (this is a WinExe app)
        if (args[0].Equals("setup", StringComparison.OrdinalIgnoreCase))
        {
            ConsoleHelper.AttachOrAlloc();
            return SetupCommand.Execute();
        }

        return args[0].ToLowerInvariant() switch
        {
            "launch"    => LaunchCommand.Execute(),
            "glow"      => GlowCommand.Execute(ParseString(args, "--color", "green"), HasFlag(args, "--from-status")),
            "kill-glow" => KillGlowCommand.Execute(),
            "status"    => StatusCommand.Execute(ParseLong(args, "--hwnd"), ParseInt(args, "--quad", -1)),
            "session-state" => SessionStateCommand.Execute(args.Length > 1 ? args[1].ToLowerInvariant() : null),
            "monday"    => MondayCommand.Execute(ParseInt(args, "--quad", -1)),
            "track"     => TrackCommand.Execute(),
            "kill-server-if-build" => KillServerIfBuildCommand.Execute(),
            _ => Do(() => PrintUsage())
        };
    }

    private static string ParseString(string[] args, string flag, string fallback)
    {
        for (int i = 1; i < args.Length - 1; i++)
        {
            if (args[i] == flag)
                return args[i + 1].ToLowerInvariant();
        }
        return fallback;
    }

    private static long ParseLong(string[] args, string flag)
    {
        for (int i = 1; i < args.Length - 1; i++)
        {
            if (args[i] == flag && long.TryParse(args[i + 1], out long val))
                return val;
        }
        return 0;
    }

    private static int ParseInt(string[] args, string flag, int fallback)
    {
        for (int i = 1; i < args.Length - 1; i++)
        {
            if (args[i] == flag && int.TryParse(args[i + 1], out int val))
                return val;
        }
        return fallback;
    }

    private static bool HasFlag(string[] args, string flag)
    {
        return args.Any(a => a == flag);
    }

    private static int Do(Action action)
    {
        action();
        return 1;
    }

    private static void PrintUsage()
    {
        // WinExe has no console by default, but this is here for completeness
    }
}
