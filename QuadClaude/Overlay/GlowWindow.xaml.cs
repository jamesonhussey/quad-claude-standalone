using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using QuadClaude.Interop;

namespace QuadClaude.Overlay;

public partial class GlowWindow : Window
{
    private readonly IntPtr _trackedHWnd;
    private readonly DispatcherTimer _timer;
    private const int ExpandSide = 4;  // px larger than terminal on left/right/bottom
    private const int ExpandTop = 6;   // px larger on top to clear title bar

    public GlowWindow(IntPtr trackedHWnd, string color)
    {
        InitializeComponent();

        _trackedHWnd = trackedHWnd;

        // Set color
        var hex = color switch
        {
            "red"    => "#FF4444",
            "yellow" => "#FFB347",
            "gray"   => "#666677",
            _ when color.StartsWith('#') => color,
            _        => "#00FF88",
        };
        BorderColor.Color = (Color)ColorConverter.ConvertFromString(hex);
        GlowEffect.Color = (Color)ColorConverter.ConvertFromString(hex);

        // Position over terminal
        PositionOverTerminal();

        // Tracking timer
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _timer.Tick += OnTimerTick;

        Loaded += OnLoaded;
        Closed += (_, _) => _timer.Stop();
    }

    private void PositionOverTerminal()
    {
        if (NativeMethods.GetWindowRect(_trackedHWnd, out RECT rect))
        {
            // GetWindowRect returns physical pixels; WPF uses DIPs.
            // Divide by the DPI scale factor to convert.
            var scale = NativeMethods.GetDpiScale(_trackedHWnd);

            Left = rect.Left / scale - ExpandSide;
            Top = rect.Top / scale - ExpandTop;
            Width = (rect.Right - rect.Left) / scale + ExpandSide * 2;
            Height = (rect.Bottom - rect.Top) / scale + ExpandTop + ExpandSide;
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Hide from Alt+Tab and Task View
        var helper2 = new WindowInteropHelper(this);
        NativeMethods.HideFromTaskView(helper2.Handle);

        // Place behind terminal in z-order
        var helper = new WindowInteropHelper(this);
        NativeMethods.SetWindowPos(
            helper.Handle, _trackedHWnd,
            0, 0, 0, 0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOACTIVATE);

        _timer.Start();
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        if (!NativeMethods.IsWindow(_trackedHWnd))
        {
            _timer.Stop();
            Close();
            return;
        }

        PositionOverTerminal();
    }
}
