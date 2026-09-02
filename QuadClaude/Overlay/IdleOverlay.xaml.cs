using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using QuadClaude.Interop;
using QuadClaude.Overlay.Animations;

namespace QuadClaude.Overlay;

public partial class IdleOverlay : Window
{
    private readonly IntPtr _trackedHWnd;
    private const int Inset = 3;
    private const double FrameInterval = 1.0 / 60.0;
    private const double DonePanelHeight = 170;

    private IIdleAnimation? _currentAnimation;
    private bool _isWorkingMode;
    private TimeSpan _lastRenderTime;
    private bool _renderingHooked;

    public bool PartyMode { get; set; }
    public bool TintEnabled { get; set; } = true;
    public double TintAmount { get; set; } = 0.25;

    public IdleOverlay(IntPtr trackedHWnd)
    {
        InitializeComponent();
        _trackedHWnd = trackedHWnd;

        MouseLeftButtonDown += OnClick;

        Loaded += (_, _) =>
        {
            var helper = new WindowInteropHelper(this);
            NativeMethods.HideFromTaskView(helper.Handle);
            PlaceAboveTerminal(helper.Handle);
        };
    }

    public void SetStatus(string label, string hexColor)
    {
        try
        {
            if (hexColor == "none") hexColor = "#666677";
            var color = (Color)ColorConverter.ConvertFromString(hexColor);
            var brush = new SolidColorBrush(color);

            WorkingStatusLabel.Text = label.ToUpperInvariant();
            WorkingStatusLabel.Foreground = brush;
            DoneStatusLabel.Text = label.ToUpperInvariant();
            DoneStatusLabel.Foreground = brush;

            // Tint each backdrop with the status hue so it matches the glow
            // border (preserving each panel's opacity: Working ~0xCC scrim,
            // Done ~0xF5 floating panel). When tinting is off, fall back to the
            // neutral dark-navy defaults.
            if (TintEnabled)
            {
                WorkingBorder.Background = new SolidColorBrush(TintBackground(color, 0xCC, TintAmount));
                DoneBorder.Background = new SolidColorBrush(TintBackground(color, 0xF5, TintAmount));
            }
            else
            {
                WorkingBorder.Background = new SolidColorBrush(Color.FromArgb(0xCC, 0x1A, 0x1A, 0x2E));
                DoneBorder.Background = new SolidColorBrush(Color.FromArgb(0xF5, 0x1A, 0x1A, 0x2E));
            }
        }
        catch { }
    }

    // Blend the border/status color into a near-black base so the backdrop
    // carries the same hue as the glow border while staying dark enough to
    // read content over. Caller supplies the panel's opacity and tint strength.
    private static Color TintBackground(Color border, byte alpha, double tint)
    {
        const byte baseR = 0x12, baseG = 0x12, baseB = 0x1E;
        byte r = (byte)(baseR + (border.R - baseR) * tint);
        byte g = (byte)(baseG + (border.G - baseG) * tint);
        byte b = (byte)(baseB + (border.B - baseB) * tint);
        return Color.FromArgb(alpha, r, g, b);
    }

    public void UpdateContent(string? title, string? branch)
    {
        var t = string.IsNullOrWhiteSpace(title) ? "No session title" : title;
        var b = string.IsNullOrWhiteSpace(branch) ? "(no branch)" : branch;

        WorkingTitleText.Text = t;
        WorkingBranchText.Text = b;
        DoneTitleText.Text = t;
        DoneBranchText.Text = b;
    }

    public void SetMode(bool isWorking)
    {
        bool modeChanged = _isWorkingMode != isWorking;
        bool windowVisible = Visibility == Visibility.Visible;

        if (!modeChanged && windowVisible)
            return;

        _isWorkingMode = isWorking;

        if (isWorking)
        {
            StopAnimation();
            WorkingPanel.Visibility = Visibility.Visible;
            DonePanel.Visibility = Visibility.Collapsed;
            PositionFullOverlay();
            StartAnimation();
        }
        else
        {
            StopAnimation();
            WorkingPanel.Visibility = Visibility.Collapsed;
            DonePanel.Visibility = Visibility.Visible;
            PositionDonePanel();
        }
    }

    public void PositionOverTerminal()
    {
        if (_isWorkingMode)
            PositionFullOverlay();
        else
            PositionDonePanel();
    }

    public void FadeIn()
    {
        Opacity = 0;
        Visibility = Visibility.Visible;
        PositionOverTerminal();

        var anim = new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300));
        BeginAnimation(OpacityProperty, anim);
    }

    public void FadeOut(Action? onComplete = null)
    {
        var anim = new System.Windows.Media.Animation.DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200));
        anim.Completed += (_, _) =>
        {
            Visibility = Visibility.Hidden;
            StopAnimation();
            onComplete?.Invoke();
        };
        BeginAnimation(OpacityProperty, anim);
    }

    private void OnClick(object sender, MouseButtonEventArgs e)
    {
        FadeOut();
        NativeMethods.SetForegroundWindow(_trackedHWnd);
    }

    private void PlaceAboveTerminal(IntPtr overlayHWnd)
    {
        IntPtr windowAboveTerminal = NativeMethods.GetWindow(_trackedHWnd, NativeMethods.GW_HWNDPREV);
        if (windowAboveTerminal != IntPtr.Zero && windowAboveTerminal != overlayHWnd)
        {
            NativeMethods.SetWindowPos(
                overlayHWnd, windowAboveTerminal,
                0, 0, 0, 0,
                NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOACTIVATE);
        }
    }

    private void PositionFullOverlay()
    {
        if (!NativeMethods.GetWindowRect(_trackedHWnd, out RECT rect)) return;

        var scale = NativeMethods.GetDpiScale(_trackedHWnd);
        Left = rect.Left / scale + Inset;
        Top = rect.Top / scale + Inset;
        Width = (rect.Right - rect.Left) / scale - Inset * 2;
        Height = (rect.Bottom - rect.Top) / scale - Inset * 2;
    }

    private void PositionDonePanel()
    {
        if (!NativeMethods.GetWindowRect(_trackedHWnd, out RECT rect)) return;

        var scale = NativeMethods.GetDpiScale(_trackedHWnd);
        double termW = (rect.Right - rect.Left) / scale - Inset * 2;
        double termH = (rect.Bottom - rect.Top) / scale - Inset * 2;
        double termLeft = rect.Left / scale + Inset;
        double termTop = rect.Top / scale + Inset;

        Width = termW;
        Height = DonePanelHeight;
        Left = termLeft;
        Top = termTop + (termH - DonePanelHeight) / 2;
    }

    private void StartAnimation()
    {
        StopAnimation();

        if (PartyMode)
        {
            if (!NativeMethods.GetWindowRect(_trackedHWnd, out RECT rect)) return;
            var scale = NativeMethods.GetDpiScale(_trackedHWnd);
            double w = (rect.Right - rect.Left) / scale - Inset * 2;
            double h = (rect.Bottom - rect.Top) / scale - Inset * 2;

            SpinnerCanvas.Visibility = Visibility.Collapsed;
            _currentAnimation = AnimationFactory.CreateRandom();
            _currentAnimation.Initialize(AnimationCanvas, w, h);
        }
        else
        {
            SpinnerCanvas.Visibility = Visibility.Visible;
            _currentAnimation = new LoadingSpinnerAnimation();
            _currentAnimation.Initialize(SpinnerCanvas, 80, 80);
        }

        _lastRenderTime = TimeSpan.Zero;

        if (!_renderingHooked)
        {
            CompositionTarget.Rendering += OnRenderFrame;
            _renderingHooked = true;
        }
    }

    private void StopAnimation()
    {
        if (_renderingHooked)
        {
            CompositionTarget.Rendering -= OnRenderFrame;
            _renderingHooked = false;
        }

        _currentAnimation?.Dispose();
        _currentAnimation = null;
        AnimationCanvas.Children.Clear();
        SpinnerCanvas.Children.Clear();
        SpinnerCanvas.Visibility = Visibility.Collapsed;
    }

    private void OnRenderFrame(object? sender, EventArgs e)
    {
        var args = (RenderingEventArgs)e;
        var now = args.RenderingTime;

        if (_lastRenderTime == TimeSpan.Zero)
        {
            _lastRenderTime = now;
            return;
        }

        var delta = (now - _lastRenderTime).TotalSeconds;
        if (delta < FrameInterval) return;

        _lastRenderTime = now;
        _currentAnimation?.Update(delta);
    }
}
