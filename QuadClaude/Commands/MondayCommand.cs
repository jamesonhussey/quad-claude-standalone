using System.Windows;
using QuadClaude.Config;
using QuadClaude.Overlay;

namespace QuadClaude.Commands;

/// <summary>
/// Debug entry point: opens the Monday panel standalone (fixed position, no quad
/// docking) so the panel and both fetch backends can be tested without launching
/// the full grid. Usage: QuadClaude.exe monday [--quad N]
/// </summary>
public static class MondayCommand
{
    public static int Execute(int quadIndex)
    {
        var config = QuadConfig.Load() ?? new QuadConfig();
        var app = new Application();
        app.Run(new MondayPanel(quadIndex < 0 ? 0 : quadIndex, config, standalone: true));
        return 0;
    }
}
