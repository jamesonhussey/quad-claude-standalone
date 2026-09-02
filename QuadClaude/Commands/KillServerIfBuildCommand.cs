using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using QuadClaude.Config;

namespace QuadClaude.Commands;

public static class KillServerIfBuildCommand
{
    private static readonly Regex BuildPattern = new(
        @"npm run (build|lint)|git push",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static int Execute()
    {
        try
        {
            var json = Console.In.ReadToEnd();
            if (string.IsNullOrEmpty(json)) return 0;

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            string? command = null;
            if (root.TryGetProperty("tool_input", out var toolInput)
                && toolInput.TryGetProperty("command", out var cmd))
            {
                command = cmd.GetString();
            }

            if (command == null || !BuildPattern.IsMatch(command))
                return 0;

            var quadIndex = Environment.GetEnvironmentVariable("QUAD_INDEX");
            if (quadIndex == null || !int.TryParse(quadIndex, out int qi))
                return 0;

            var config = QuadConfig.Load();
            var ports = config?.DevServerPorts ?? [3000, 3001, 3002, 3003];
            int port = qi >= 0 && qi < ports.Length ? ports[qi] : 3000 + qi;

            QuadLauncher.StopDevServer(port);
            Thread.Sleep(500);
        }
        catch { }
        return 0;
    }
}
