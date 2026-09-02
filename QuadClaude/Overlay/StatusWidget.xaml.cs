using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using QuadClaude.Config;
using QuadClaude.Data;
using QuadClaude.Interop;

namespace QuadClaude.Overlay;

public partial class StatusWidget : Window
{
    private IntPtr _trackedHWnd;
    private readonly int _quadIndex;
    private readonly DispatcherTimer _timer;
    private readonly string _stateFile;
    private readonly string _cwdFile;
    private int _phaseIndex;
    private bool _optionsExpanded;
    private string _lastCwdJson = "";
    private string _sizeMode = "M"; // S, M, L
    private FileExplorerPanel? _explorerPanel;
    private int _serverPort;
    private bool _serverRunning;
    private bool _initialized;

    // Idle overlay
    private IdleOverlay? _idleOverlay;
    private int _idleTickCount;
    private int _transcriptPollCounter;
    private string? _currentSessionId;
    private string? _currentTranscriptPath;
    private TranscriptData? _cachedTranscript;
    private long _lastTranscriptOffset;
    private string? _lastCwd;
    // Session state the overlay is currently showing ("busy"/"done"/"needs-input").
    // Tracked so the fast per-tick SyncOverlayStatus only re-applies on an actual
    // transition. Null when the overlay isn't visible, forcing a re-apply on show.
    private string? _overlayState;

    private static readonly Dictionary<string, double> SizeScales = new()
    {
        ["S"] = 0.8,
        ["M"] = 1.0,
        ["L"] = 1.25,
    };

    private static readonly (string Name, string Color, string GlowColor)[] Phases =
    {
        ("Ready",   "#00FF88", "green"),
        ("Blocked", "#FF4444", "red"),
        ("Idle",    "#666677", "gray"),
    };

    public StatusWidget(IntPtr trackedHWnd, int quadIndex)
    {
        InitializeComponent();

        _trackedHWnd = trackedHWnd;
        _quadIndex = quadIndex;

        // Persistence keyed by quad index
        var dir = PathHelper.AppDataDir;
        Directory.CreateDirectory(dir);
        _stateFile = Path.Combine(dir, quadIndex >= 0
            ? $"status-quad-{quadIndex}.json"
            : "status-default.json");
        _cwdFile = Path.Combine(dir, quadIndex >= 0
            ? $"quad-{quadIndex}.cwd.json"
            : "quad-default.cwd.json");

        LoadState();
        UpdatePhaseVisual();
        ApplySizeMode();
        InitServerPanel();
        var initConfig = QuadConfig.Load();
        UpdateLayoutHighlight(initConfig?.WindowLayout ?? "grid");
        UpdateGlowSwatches();
        IdleOverlayCheck.IsChecked = initConfig?.IdleOverlayEnabled ?? true;
        PartyModeCheck.IsChecked = initConfig?.PartyModeEnabled ?? false;
        CarouselCheck.IsChecked = initConfig?.CarouselModeEnabled ?? false;
        IdleTintCheck.IsChecked = initConfig?.IdleTintEnabled ?? true;
        IdleTintSlider.Value = initConfig?.IdleTintAmount ?? 0.25;

        // Monday panel is opt-in (config.mondayEnabled, default off). When off,
        // hide its button and its settings toggle so the overlay has no Monday
        // surface at all — overrides whatever LoadState() restored above.
        if (initConfig?.MondayEnabled != true)
        {
            ShowMondayCheck.IsChecked = false;
            ShowMondayCheck.Visibility = Visibility.Collapsed;
            MondayBtn.Visibility = Visibility.Collapsed;
        }

        _initialized = true;
        ReadCwdState(); // Initial read
        PositionWidget();

        // Track the terminal window — follow position, read cwd state
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _timer.Tick += OnTimerTick;

        LabelBox.TextChanged += (_, _) => SaveState();
        LabelBox.KeyDown += OnLabelKeyDown;

        Loaded += (_, _) =>
        {
            var helper = new WindowInteropHelper(this);

            // Hide from Alt+Tab and Task View
            NativeMethods.HideFromTaskView(helper.Handle);

            // Place just above the tracked terminal (not topmost over everything)
            PlaceAboveTerminal(helper.Handle);

            _timer.Start();
        };
        Closed += (_, _) =>
        {
            _timer.Stop();
            _explorerPanel?.Close();
            _idleOverlay?.Close();
        };
    }

    // ────────────────────────────────────────────────────────────
    //  Timer — position tracking + cwd polling
    // ────────────────────────────────────────────────────────────

    private int _cwdPollCounter;

    private void OnTimerTick(object? sender, EventArgs e)
    {
        if (!NativeMethods.IsWindow(_trackedHWnd))
        {
            _timer.Stop();
            Close();
            return;
        }

        PositionWidget();

        // Keep widget just above its terminal in z-order
        var helper = new WindowInteropHelper(this);
        PlaceAboveTerminal(helper.Handle);

        // Poll cwd file every ~2 seconds (4 ticks at 500ms)
        _cwdPollCounter++;
        if (_cwdPollCounter >= 4)
        {
            _cwdPollCounter = 0;
            ReadCwdState();
            UpdateServerDot();
        }

        CheckIdleState();

        // Fast working/done flip — every tick (~500ms), independent of the slow
        // transcript poll below, so the overlay doesn't linger on "WORKING".
        SyncOverlayStatus();

        // Carousel focus mode: rotate a working focus quad out for an idle one.
        CarouselTick(QuadConfig.Load() ?? new QuadConfig());

        // Poll transcript every ~5 seconds (10 ticks)
        _transcriptPollCounter++;
        if (_transcriptPollCounter >= 10)
        {
            _transcriptPollCounter = 0;
            RefreshTranscriptData();
        }
    }

    // Available widget width below which the secondary buttons collapse into the ⋯
    // overflow popup. Above it, they render inline.
    private const double OverflowThreshold = 520;
    private bool _overflowActive;

    // Show the secondary buttons inline when there's room, else move them into the
    // overflow popup and reveal the ⋯ toggle. Also tightens the label/branch widths
    // when space is tight so the primary row itself fits.
    private void ApplyResponsiveBar(double avail)
    {
        bool overflow = avail < OverflowThreshold;

        LabelBox.MaxWidth = overflow ? 70 : 120;
        BranchBox.MaxWidth = overflow ? 90 : 160;

        if (overflow == _overflowActive) return;
        _overflowActive = overflow;

        if (overflow)
        {
            MoveChildren(SecondaryInline, SecondaryPopupHost);
            MoreBtn.Visibility = Visibility.Visible;
        }
        else
        {
            OverflowPopup.IsOpen = false;
            MoveChildren(SecondaryPopupHost, SecondaryInline);
            MoreBtn.Visibility = Visibility.Collapsed;
        }
    }

    private static void MoveChildren(Panel from, Panel to)
    {
        while (from.Children.Count > 0)
        {
            var child = from.Children[0];
            from.Children.RemoveAt(0);
            to.Children.Add(child);
        }
    }

    private void OnMoreClick(object sender, MouseButtonEventArgs e)
        => OverflowPopup.IsOpen = !OverflowPopup.IsOpen;

    private void PositionWidget()
    {
        if (!NativeMethods.GetWindowRect(_trackedHWnd, out RECT rect)) return;

        // GetWindowRect returns physical pixels; WPF uses DIPs.
        var scale = NativeMethods.GetDpiScale(_trackedHWnd);
        double left = rect.Left / scale;
        double right = rect.Right / scale;
        double bottom = rect.Bottom / scale;
        double quadWidth = right - left;

        // Cap widget width so it doesn't dominate small screens, but allow enough
        // room on wide quads to show the whole bar inline.
        double avail = Math.Clamp(quadWidth * 0.9, 200, 700);
        MaxWidth = avail;
        ApplyResponsiveBar(avail);

        // Bottom-right corner, inset 8px from edges
        Left = right - ActualWidth - 8;
        Top = bottom - ActualHeight - 8;

        // Fallback before first render
        if (ActualWidth == 0)
            Left = right - 280 - 8;
        if (ActualHeight == 0)
            Top = bottom - 36;
    }

    /// <summary>
    /// Place this widget just above the tracked terminal in z-order.
    /// Check if the widget is already directly above the terminal;
    /// if not, move it there without disturbing the terminal's position.
    /// </summary>
    private void PlaceAboveTerminal(IntPtr widgetHWnd)
    {
        // Check: is the widget already directly above the terminal?
        IntPtr windowAboveTerminal = NativeMethods.GetWindow(_trackedHWnd, NativeMethods.GW_HWNDPREV);
        if (windowAboveTerminal == widgetHWnd)
            return; // Already in the right spot — do nothing

        // Move the widget to be directly above the terminal.
        // SetWindowPos 2nd param = "insert after this window" (i.e., behind it).
        // So we find what's currently above the terminal and insert the widget after that,
        // which places the widget between that window and the terminal.
        if (windowAboveTerminal != IntPtr.Zero)
        {
            NativeMethods.SetWindowPos(
                widgetHWnd, windowAboveTerminal,
                0, 0, 0, 0,
                NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOACTIVATE);
        }
    }

    // ────────────────────────────────────────────────────────────
    //  Branch / Directory tracking
    // ────────────────────────────────────────────────────────────

    private void ReadCwdState()
    {
        try
        {
            if (!File.Exists(_cwdFile)) return;
            var json = File.ReadAllText(_cwdFile);
            if (json == _lastCwdJson) return; // no change
            _lastCwdJson = json;

            var state = JsonSerializer.Deserialize<CwdState>(json);
            if (state == null) return;

            // Compact bar
            BranchText.Text = string.IsNullOrEmpty(state.Branch) ? "(no branch)" : state.Branch;

            // Show project name + shortened dir path
            var dir = state.Cwd ?? "";
            var project = state.Project ?? "";
            if (!string.IsNullOrEmpty(dir) && dir != project)
            {
                // Show last two path segments: "Projects/my-repo"
                var parts = dir.Replace('\\', '/').TrimEnd('/').Split('/');
                var shortDir = parts.Length >= 2
                    ? $"{parts[^2]}/{parts[^1]}"
                    : parts[^1];
                ProjectText.Text = shortDir;
                ProjectText.ToolTip = dir;
            }
            else
            {
                ProjectText.Text = project;
            }

            // Options panel details
            DetailBranch.Text = $"Branch: {state.Branch ?? "n/a"}";
            DetailDir.Text = $"Dir: {state.Cwd ?? "n/a"}";

            // Track cwd and session for idle overlay
            _lastCwd = state.Cwd;

            // A handed-off / idle quad sits on a detached HEAD (or has no branch). Clear
            // the cached summary so it stops showing the previous task's title. Done BEFORE
            // the sessionId check below so an actually-active quad whose state carries a
            // fresh sessionId still re-populates on this same pass. An active quad on a real
            // branch is unaffected (detached == false).
            var branch = state.Branch;
            bool detached = string.IsNullOrEmpty(branch)
                || branch.Contains("(detached)")
                || branch.StartsWith("detached ", StringComparison.Ordinal);
            if (detached)
            {
                _currentSessionId = null;
                _currentTranscriptPath = null;
                _cachedTranscript = null;
                _lastTranscriptOffset = 0;
            }

            if (!string.IsNullOrEmpty(state.SessionId) && state.SessionId != _currentSessionId)
            {
                _currentSessionId = state.SessionId;
                _currentTranscriptPath = null;
                _cachedTranscript = null;
                _lastTranscriptOffset = 0;
            }
        }
        catch { /* file being written — skip this cycle */ }
    }

    // ────────────────────────────────────────────────────────────
    //  Idle overlay
    // ────────────────────────────────────────────────────────────

    private void CheckIdleState()
    {
        var config = QuadConfig.Load();
        if (config != null && !config.IdleOverlayEnabled)
        {
            if (_idleOverlay?.Visibility == Visibility.Visible)
                _idleOverlay.FadeOut();
            _idleTickCount = 0;
            return;
        }

        var fg = NativeMethods.GetForegroundWindow();
        if (fg == _trackedHWnd)
        {
            _idleTickCount = 0;
            if (_idleOverlay?.Visibility == Visibility.Visible)
                _idleOverlay.FadeOut();
            return;
        }

        _idleTickCount++;
        int delaySeconds = config?.IdleOverlayDelaySeconds ?? 30;
        int thresholdTicks = delaySeconds * 2; // timer fires every 500ms

        if (_idleTickCount >= thresholdTicks)
            ShowIdleOverlay();
    }

    private void ShowIdleOverlay()
    {
        if (_idleOverlay != null && _idleOverlay.Visibility == Visibility.Visible)
        {
            _idleOverlay.PositionOverTerminal();
            return;
        }

        if (_idleOverlay == null)
        {
            _idleOverlay = new IdleOverlay(_trackedHWnd);
            _idleOverlay.Closed += (_, _) => _idleOverlay = null;
        }

        RefreshTranscriptData();

        string? branch = null;
        try
        {
            if (!string.IsNullOrEmpty(_lastCwdJson))
            {
                using var doc = JsonDocument.Parse(_lastCwdJson);
                if (doc.RootElement.TryGetProperty("branch", out var bp))
                    branch = bp.GetString();
            }
        }
        catch { }

        var glowConfig = QuadConfig.Load();
        _idleOverlay.PartyMode = glowConfig?.PartyModeEnabled ?? false;
        _idleOverlay.UpdateContent(
            _cachedTranscript?.AiTitle,
            branch);

        // Tint, colour, label, and panel mode all derive from the current
        // hook-written session state.
        SetOverlayVisualsForState(GetSessionState());
        _idleOverlay.Show();
        _idleOverlay.FadeIn();

        // Place overlay above terminal in z-order
        var helper = new WindowInteropHelper(_idleOverlay);
        if (helper.Handle != IntPtr.Zero)
        {
            NativeMethods.HideFromTaskView(helper.Handle);
            IntPtr above = NativeMethods.GetWindow(_trackedHWnd, NativeMethods.GW_HWNDPREV);
            if (above != IntPtr.Zero)
            {
                NativeMethods.SetWindowPos(
                    helper.Handle, above,
                    0, 0, 0, 0,
                    NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOACTIVATE);
            }
        }
    }

    // Live activity state for this quad, written by hooks (see SessionStateCommand):
    // "busy" | "needs-input" | "done". Read from the per-quad state file rather than
    // Claude's internal session files, whose format is undocumented and dropped the
    // old "busy" status field in current versions. Defaults to "done" when absent.
    private string GetSessionState()
    {
        try
        {
            var file = Path.Combine(PathHelper.AppDataDir, $"session-state-quad-{_quadIndex}.json");
            if (!File.Exists(file)) return "done";
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var doc = JsonDocument.Parse(stream);
            if (doc.RootElement.TryGetProperty("state", out var s))
            {
                var v = s.GetString();
                if (v is "busy" or "needs-input" or "done") return v;
            }
        }
        catch { /* file being written — treat as unchanged this cycle */ }
        return "done";
    }

    // Maps a session state to the overlay's panel mode, label, and colour.
    // "busy" uses the full working scrim; "done"/"needs-input" use the compact
    // waiting card, differing only by colour + label.
    private static (bool workingPanel, string label, string colorHex) OverlayVisualForState(string state, QuadConfig? config)
    {
        return state switch
        {
            "busy"        => (true,  "Working",     config?.GlowColorWorking    ?? "#FFB347"),
            "needs-input" => (false, "Needs Input", config?.GlowColorNeedsInput ?? "#FF4D4D"),
            _             => (false, "Done",        config?.GlowColorDone       ?? "#00FF88"),
        };
    }

    // Applies the given state's tint/colour/label/mode to the visible overlay and
    // records it in _overlayState. Single place that drives overlay appearance.
    private void SetOverlayVisualsForState(string state)
    {
        if (_idleOverlay == null) return;
        var config = QuadConfig.Load();
        var (workingPanel, label, colorHex) = OverlayVisualForState(state, config);
        _idleOverlay.TintEnabled = config?.IdleTintEnabled ?? true;
        _idleOverlay.TintAmount = config?.IdleTintAmount ?? 0.25;
        _idleOverlay.SetStatus(label, colorHex);
        _idleOverlay.SetMode(workingPanel);
        _overlayState = state;
    }

    // Cheap per-tick check that keeps the idle overlay in sync with the hook-written
    // session state, so the overlay flips the instant Claude's state changes rather
    // than lagging behind. No-op unless the state actually changed.
    private void SyncOverlayStatus()
    {
        if (_idleOverlay == null || _idleOverlay.Visibility != Visibility.Visible)
        {
            _overlayState = null;
            return;
        }

        var state = GetSessionState();
        if (_overlayState == state) return;
        SetOverlayVisualsForState(state);
    }

    // ────────────────────────────────────────────────────────────
    //  Carousel focus mode
    // ────────────────────────────────────────────────────────────

    // Last observed state, for detecting this quad's transition into "busy".
    private string? _carouselLastState;

    // When the quad in the big focus pane starts working, demote it and pull the
    // idle quad furthest back in the queue up into focus. Fires only on the busy
    // transition (never on finish) and only pulls a non-busy quad; if none is idle,
    // the working quad stays put. Focus-layout only, and only when the mode is on.
    private void CarouselTick(QuadConfig config)
    {
        if (!config.CarouselModeEnabled || config.WindowLayout != "focus")
        {
            _carouselLastState = null; // reset so re-enabling won't fire on a stale edge
            return;
        }

        var state = GetSessionState();
        bool becameBusy = state == "busy" && _carouselLastState is not null && _carouselLastState != "busy";
        _carouselLastState = state;
        if (!becameBusy) return;

        // Only the quad currently in the big focus slot triggers the swap.
        var order = QuadLauncher.ReadFocusOrder();
        if (order.Length == 0 || order[0] != _quadIndex) return;

        // Pull the idle (non-busy) quad furthest back in the queue into focus.
        for (int slot = order.Length - 1; slot >= 1; slot--)
        {
            int q = order[slot];
            if (!QuadLauncher.QuadWindowLive(q)) continue;
            if (QuadLauncher.ReadSessionState(q) == "busy") continue;
            QuadLauncher.PromoteToFocus(q);
            MoveAllQuadsToLayout(config, animate: true);
            break;
        }
    }

    private void RefreshTranscriptData()
    {
        if (string.IsNullOrEmpty(_lastCwd)) return;

        // Resolve session ID if we don't have one (quad-aware: skip sessions other
        // quads are already showing so a shared cwd doesn't bleed the wrong title).
        if (string.IsNullOrEmpty(_currentSessionId))
            _currentSessionId = SessionDataService.FindSessionIdForQuad(_quadIndex, _lastCwd);

        if (string.IsNullOrEmpty(_currentSessionId)) return;

        // Resolve transcript path if we don't have one
        if (string.IsNullOrEmpty(_currentTranscriptPath))
            _currentTranscriptPath = SessionDataService.FindTranscriptPath(_currentSessionId, _lastCwd);

        if (string.IsNullOrEmpty(_currentTranscriptPath)) return;

        try
        {
            _cachedTranscript = SessionDataService.ParseTranscript(_currentTranscriptPath, _lastTranscriptOffset);
            _lastTranscriptOffset = _cachedTranscript.FileOffset;

            // Update the overlay if it's showing
            if (_idleOverlay?.Visibility == Visibility.Visible)
            {
                string? branch = null;
                try
                {
                    if (!string.IsNullOrEmpty(_lastCwdJson))
                    {
                        using var doc = JsonDocument.Parse(_lastCwdJson);
                        if (doc.RootElement.TryGetProperty("branch", out var bp))
                            branch = bp.GetString();
                    }
                }
                catch { }

                // Only the transcript title/branch is refreshed here; the overlay's
                // colour/label/mode are driven by SyncOverlayStatus every tick.
                _idleOverlay.UpdateContent(
                    _cachedTranscript.AiTitle,
                    branch);
                _idleOverlay.PartyMode = QuadConfig.Load()?.PartyModeEnabled ?? false;
            }
        }
        catch { }
    }

    // ────────────────────────────────────────────────────────────
    //  Phase cycling
    // ────────────────────────────────────────────────────────────

    private void OnPhaseClick(object sender, MouseButtonEventArgs e)
    {
        _phaseIndex = (_phaseIndex + 1) % Phases.Length;
        UpdatePhaseVisual();
        SaveState();
        SpawnGlowForPhase();
    }

    private void SpawnGlowForPhase()
    {
        var glowColor = Phases[_phaseIndex].GlowColor;
        var exePath = Environment.ProcessPath ?? "QuadClaude.exe";
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = $"glow --color {glowColor}",
                UseShellExecute = false,
                Environment = { ["QUAD_INDEX"] = _quadIndex.ToString(), ["QUADCLAUDE_INSTANCE"] = QuadClaude.Config.PathHelper.InstanceName }
            });
        }
        catch { }
    }

    private void UpdatePhaseVisual()
    {
        var (name, colorHex, _) = Phases[_phaseIndex];
        var color = (Color)ColorConverter.ConvertFromString(colorHex);

        PhaseText.Text = name;
        PhaseDot.Fill = new SolidColorBrush(color);

        // Tint the entire toolbar background with a subtle version of the phase color
        // Blend: mostly the dark base (#1E1E2E) with a hint of the phase color
        var tinted = Color.FromArgb(
            0xDD, // same opacity as original
            (byte)(0x1E + (color.R - 0x1E) * 0.15),
            (byte)(0x1E + (color.G - 0x1E) * 0.15),
            (byte)(0x2E + (color.B - 0x2E) * 0.15));
        RootBorder.Background = new SolidColorBrush(tinted);

        // Also tint the border with a subtle glow of the phase color
        RootBorder.BorderThickness = new Thickness(1);
        RootBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(0x44, color.R, color.G, color.B));
    }

    // ────────────────────────────────────────────────────────────
    //  Options panel
    // ────────────────────────────────────────────────────────────

    private void OnExplorerClick(object sender, MouseButtonEventArgs e)
    {
        if (_explorerPanel != null && _explorerPanel.IsVisible)
        {
            _explorerPanel.Close();
            _explorerPanel = null;
            return;
        }

        // Get CWD from the last known state
        string? rootPath = null;
        try
        {
            if (!string.IsNullOrEmpty(_lastCwdJson))
            {
                using var doc = System.Text.Json.JsonDocument.Parse(_lastCwdJson);
                if (doc.RootElement.TryGetProperty("cwd", out var cwdProp))
                    rootPath = cwdProp.GetString();
            }
        }
        catch { }

        if (string.IsNullOrEmpty(rootPath)) return;

        // Normalize MSYS paths (/c/Projects/...) to Windows paths
        if (rootPath.Length >= 3 && rootPath[0] == '/' && rootPath[2] == '/')
        {
            rootPath = $"{char.ToUpper(rootPath[1])}:{rootPath[2..]}";
        }
        rootPath = rootPath.Replace('/', '\\');

        _explorerPanel = new FileExplorerPanel(_trackedHWnd, _quadIndex, rootPath, this);
        _explorerPanel.Closed += (_, _) => _explorerPanel = null;
        _explorerPanel.Show();
    }

    private void OnMondayClick(object sender, MouseButtonEventArgs e)
    {
        var config = QuadConfig.Load() ?? new QuadConfig();
        MondayPanel.Toggle(_quadIndex, config);
    }

    private void OnGearClick(object sender, MouseButtonEventArgs e)
    {
        _optionsExpanded = !_optionsExpanded;
        OptionsPanel.Visibility = _optionsExpanded ? Visibility.Visible : Visibility.Collapsed;

        if (_optionsExpanded)
        {
            UpdateQuadButtonStates();
            UpdateFifthElementLabel();
            PopulateMonitorButtons();
            UpdateGlowSwatches();
        }

        // Reposition after size change (panel expands upward)
        Dispatcher.BeginInvoke(() => PositionWidget(), DispatcherPriority.Loaded);
    }

    /// <summary>
    /// Check which quads have live terminals and style buttons accordingly.
    /// Active quads are dimmed, missing quads are highlighted.
    /// </summary>
    private void UpdateQuadButtonStates()
    {
        var appDataDir = PathHelper.AppDataDir;

        Border[] buttons = [QuadBtn0, QuadBtn1, QuadBtn2, QuadBtn3];

        for (int i = 0; i < 4; i++)
        {
            var hwndFile = Path.Combine(appDataDir, $"quad-{i}.hwnd");
            bool alive = false;

            if (File.Exists(hwndFile))
            {
                try
                {
                    var hwndVal = long.Parse(File.ReadAllText(hwndFile).Trim());
                    alive = NativeMethods.IsWindow(new IntPtr(hwndVal));
                }
                catch { }
            }

            var btn = buttons[i];
            var text = (TextBlock)btn.Child;

            if (alive)
            {
                // Active quad — dim it
                btn.Background = new SolidColorBrush(Color.FromArgb(0x11, 0xFF, 0xFF, 0xFF));
                text.Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0x66, 0x66, 0x77));
                btn.Cursor = Cursors.Arrow;
            }
            else
            {
                // Missing quad — highlight as openable
                btn.Background = new SolidColorBrush(Color.FromArgb(0x33, 0x00, 0xFF, 0x88));
                text.Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0x00, 0xFF, 0x88));
                btn.Cursor = Cursors.Hand;
            }
        }
    }

    private void OnSizeClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border border || border.Tag is not string size) return;
        _sizeMode = size;
        ApplySizeMode();
        SaveState();

        // Reposition after scale change
        Dispatcher.BeginInvoke(() => PositionWidget(), DispatcherPriority.Loaded);
    }

    private void ApplySizeMode()
    {
        double scale = SizeScales.GetValueOrDefault(_sizeMode, 1.0);
        RootBorder.LayoutTransform = new ScaleTransform(scale, scale);

        // Highlight the active size button
        var activeColor = new SolidColorBrush(Color.FromArgb(0x44, 0x00, 0xFF, 0x88));
        var normalColor = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF));

        SizeBtnS.Background = _sizeMode == "S" ? activeColor : normalColor;
        SizeBtnM.Background = _sizeMode == "M" ? activeColor : normalColor;
        SizeBtnL.Background = _sizeMode == "L" ? activeColor : normalColor;
    }

    private void UpdateFifthElementLabel()
    {
        var appDataDir = PathHelper.AppDataDir;

        // Count live overflow terminals
        int liveCount = 0;
        for (int i = 5; i < 100; i++)
        {
            var hwndFile = Path.Combine(appDataDir, $"quad-{i}.hwnd");
            if (!File.Exists(hwndFile)) break;
            try
            {
                var val = long.Parse(File.ReadAllText(hwndFile).Trim());
                if (NativeMethods.IsWindow(new IntPtr(val)))
                    liveCount++;
            }
            catch { }
        }

        FifthElementText.Text = liveCount == 0 ? "+ 5th" : $"+ 5.{liveCount}";
    }

    private void OnOpenQuadClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border border || border.Tag is not string tagStr) return;
        if (!int.TryParse(tagStr, out int targetQuad)) return;

        // Check if that quad already has a live terminal
        var appDataDir = PathHelper.AppDataDir;
        var hwndFile = Path.Combine(appDataDir, $"quad-{targetQuad}.hwnd");

        if (File.Exists(hwndFile))
        {
            try
            {
                var hwndVal = long.Parse(File.ReadAllText(hwndFile).Trim());
                if (NativeMethods.IsWindow(new IntPtr(hwndVal)))
                    return; // Already alive — don't open another
            }
            catch { }
        }

        // Launch on background thread
        var thread = new Thread(() =>
        {
            var newHWnd = QuadLauncher.LaunchSingleQuad(targetQuad);
            if (newHWnd != IntPtr.Zero)
            {
                QuadLauncher.SpawnStatusWidget(newHWnd, targetQuad);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        // Refresh button states
        var refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        refreshTimer.Tick += (_, _) =>
        {
            refreshTimer.Stop();
            UpdateQuadButtonStates();
        };
        refreshTimer.Start();
    }

    private void OnFifthElementClick(object sender, MouseButtonEventArgs e)
    {
        // Find the next available overflow slot (5, 6, 7, ...)
        var appDataDir = PathHelper.AppDataDir;
        Directory.CreateDirectory(appDataDir);

        int slotId = 5;
        while (slotId < 100) // safety cap
        {
            var hwndFile = Path.Combine(appDataDir, $"quad-{slotId}.hwnd");
            if (!File.Exists(hwndFile))
                break;

            try
            {
                var val = long.Parse(File.ReadAllText(hwndFile).Trim());
                if (!NativeMethods.IsWindow(new IntPtr(val)))
                {
                    // Dead window — reuse this slot
                    break;
                }
            }
            catch { break; }

            slotId++;
        }

        int capturedSlot = slotId;
        int overflowCount = slotId - 5; // how many are already open (for stagger offset)

        var thread = new Thread(() =>
        {
            var config = QuadConfig.Load();
            string setupDir = config?.SetupDir ?? QuadLauncher.FindSetupDirFallback();
            string shellExe = config?.ShellExe ?? @"C:\Program Files\Git\bin\bash.exe";
            string shellType = config?.ShellType ?? "gitbash";
            string terminalProfile = config?.TerminalProfile ?? "Git Bash";
            string projectsDir = config?.ProjectsDir ?? Path.Combine(PathHelper.HomeDir, "Projects");
            string launchScript = PathHelper.ToMsysPath(Path.Combine(setupDir, "claude-launch.sh"));

            var args = QuadLauncher.BuildTerminalArgs(
                shellType, terminalProfile, projectsDir, shellExe, launchScript, capturedSlot);

            var existingHandles = QuadLauncher.GetAllTerminalWindowHandles();

            Process.Start(new ProcessStartInfo
            {
                FileName = "wt",
                Arguments = args,
                UseShellExecute = true
            });

            IntPtr newHWnd = IntPtr.Zero;
            var deadline = DateTime.Now.AddSeconds(10);
            while (DateTime.Now < deadline)
            {
                Thread.Sleep(300);
                var currentHandles = QuadLauncher.GetAllTerminalWindowHandles();
                foreach (var h in currentHandles)
                {
                    if (!existingHandles.Contains(h))
                    {
                        newHWnd = h;
                        break;
                    }
                }
                if (newHWnd != IntPtr.Zero) break;
            }

            if (newHWnd == IntPtr.Zero) return;

            // Center on screen, stagger each overflow slightly so they don't stack
            Thread.Sleep(200);
            var work = QuadLauncher.GetTargetMonitorWorkArea();
            int winW = (work.Right - work.Left) / 2;
            int winH = (work.Bottom - work.Top) / 2;
            int stagger = overflowCount * 30;
            int winX = work.Left + (work.Right - work.Left - winW) / 2 + stagger;
            int winY = work.Top + (work.Bottom - work.Top - winH) / 2 + stagger;
            NativeMethods.MoveWindow(newHWnd, winX, winY, winW, winH, true);

            File.WriteAllText(
                Path.Combine(appDataDir, $"quad-{capturedSlot}.hwnd"),
                newHWnd.ToInt64().ToString());

            QuadLauncher.SpawnStatusWidget(newHWnd, capturedSlot);
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    private void OnRestartClick(object sender, MouseButtonEventArgs e)
    {
        if (_quadIndex < 0) return;

        // Close current terminal
        QuadLauncher.CloseTerminal(_trackedHWnd);

        // Wait briefly, then launch new one in same quadrant
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();

            // Launch on background thread (blocks waiting for window)
            var qi = _quadIndex;
            var thread = new Thread(() =>
            {
                var newHWnd = QuadLauncher.LaunchSingleQuad(qi);
                if (newHWnd != IntPtr.Zero)
                {
                    // Spawn new status widget for the new terminal
                    QuadLauncher.SpawnStatusWidget(newHWnd, qi);
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();

            // Close this widget (the new terminal gets its own)
            Close();
        };
        timer.Start();
    }

    // ────────────────────────────────────────────────────────────
    //  Monitor selector
    // ────────────────────────────────────────────────────────────

    private List<MONITORINFO> _monitors = new();

    private void PopulateMonitorButtons()
    {
        // Remove old dynamic buttons (keep the "Monitor:" label)
        while (MonitorRow.Children.Count > 1)
            MonitorRow.Children.RemoveAt(MonitorRow.Children.Count - 1);

        _monitors.Clear();
        NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero,
            (IntPtr hMon, IntPtr hdc, ref RECT rc, IntPtr data) =>
            {
                var mi = new MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>() };
                if (NativeMethods.GetMonitorInfo(hMon, ref mi))
                    _monitors.Add(mi);
                return true;
            }, IntPtr.Zero);

        // Sort left-to-right by X position to match Windows Settings display order
        _monitors.Sort((a, b) => a.rcMonitor.Left.CompareTo(b.rcMonitor.Left));

        var config = QuadConfig.Load();
        var current = config?.TargetMonitor ?? "largest";

        for (int i = 0; i < _monitors.Count; i++)
        {
            var m = _monitors[i];
            bool isPrimary = (m.dwFlags & MONITORINFO.MONITORINFOF_PRIMARY) != 0;
            int w = m.rcMonitor.Right - m.rcMonitor.Left;
            int h = m.rcMonitor.Bottom - m.rcMonitor.Top;

            bool isSelected = current switch
            {
                "primary" => isPrimary,
                "secondary" => !isPrimary,
                _ => false
            };

            var label = $"{i + 1}";
            var tooltip = $"Display {i + 1}: {w}x{h}{(isPrimary ? " (Primary)" : "")}";

            var text = new TextBlock
            {
                Text = label,
                Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0xEE, 0xEE, 0xF0)),
                FontSize = 10,
                FontFamily = new FontFamily("Segoe UI")
            };

            var btn = new Border
            {
                Background = isSelected
                    ? new SolidColorBrush(Color.FromArgb(0x44, 0x00, 0xFF, 0x88))
                    : new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 3, 8, 3),
                Margin = new Thickness(2, 0, 0, 0),
                Cursor = Cursors.Hand,
                Tag = i,
                ToolTip = tooltip,
                Child = text
            };

            btn.MouseLeftButtonDown += OnMonitorClick;
            MonitorRow.Children.Add(btn);
        }
    }

    private void OnMonitorClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border border || border.Tag is not int monitorIndex) return;
        if (monitorIndex < 0 || monitorIndex >= _monitors.Count) return;

        var m = _monitors[monitorIndex];
        bool isPrimary = (m.dwFlags & MONITORINFO.MONITORINFOF_PRIMARY) != 0;
        var targetValue = isPrimary ? "primary" : "secondary";

        // Save to config
        var config = QuadConfig.Load() ?? new QuadConfig();
        config.TargetMonitor = targetValue;
        config.Save();

        // Move all live quad windows to the new monitor's layout
        MoveAllQuadsToLayout(config);

        // Refresh button highlight
        PopulateMonitorButtons();
    }

    // ────────────────────────────────────────────────────────────
    //  Layout
    // ────────────────────────────────────────────────────────────

    private void OnLayoutClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement el || el.Tag is not string layout) return;

        var config = QuadConfig.Load() ?? new QuadConfig();
        config.WindowLayout = layout;
        config.Save();

        MoveAllQuadsToLayout(config);
        UpdateLayoutHighlight(layout);
    }

    // Star button: promote this quad into the big focus pane. Always lands you in the
    // focus layout (switching to it if needed), then shuffles the others down keeping
    // their relative order, and repositions all windows.
    private void OnPromoteFocusClick(object sender, MouseButtonEventArgs e)
    {
        var config = QuadConfig.Load() ?? new QuadConfig();

        if (config.WindowLayout != "focus")
        {
            config.WindowLayout = "focus";
            config.Save();
            UpdateLayoutHighlight("focus");
        }

        QuadLauncher.PromoteToFocus(_quadIndex);
        MoveAllQuadsToLayout(config, animate: true);
    }

    private void UpdateLayoutHighlight(string layout)
    {
        var active = (Color)ColorConverter.ConvertFromString("#4400CCFF");
        var inactive = (Color)ColorConverter.ConvertFromString("#22FFFFFF");
        LayoutBtnGrid.Background = new SolidColorBrush(layout == "grid" ? active : inactive);
        LayoutBtnColumns.Background = new SolidColorBrush(layout == "columns" ? active : inactive);
        LayoutBtnFocus.Background = new SolidColorBrush(layout == "focus" ? active : inactive);
        LayoutBtnDual.Background = new SolidColorBrush(layout == "dual" ? active : inactive);
        LayoutBtnTwoUp.Background = new SolidColorBrush(layout == "two-up" ? active : inactive);
        LayoutBtnRows.Background = new SolidColorBrush(layout == "rows" ? active : inactive);
    }

    // Slide/resize animation state for the animated reposition path.
    private static DispatcherTimer? _moveAnimTimer;

    private static void MoveAllQuadsToLayout(QuadConfig config, bool animate = false)
    {
        var (posX, posY, w, h) = QuadLauncher.GetGridPositions(config);
        var appDataDir = PathHelper.AppDataDir;

        // Only reposition the windows this layout actually uses; e.g. switching to
        // "two-up" leaves any extra open quads where they are instead of stacking them.
        int count = QuadLauncher.WindowCountForLayout(config.WindowLayout);

        // Collect the live windows and their target rects (all in physical pixels —
        // both GetWindowRect and GetGridPositions work in physical coords).
        var moves = new List<(IntPtr hWnd, RECT from, int tx, int ty, int tw, int th)>();
        for (int i = 0; i < count; i++)
        {
            var hwndFile = Path.Combine(appDataDir, $"quad-{i}.hwnd");
            if (!File.Exists(hwndFile)) continue;
            try
            {
                var hWnd = new IntPtr(long.Parse(File.ReadAllText(hwndFile).Trim()));
                if (!NativeMethods.IsWindow(hWnd)) continue;
                NativeMethods.ShowWindow(hWnd, NativeMethods.SW_RESTORE);
                if (animate && NativeMethods.GetWindowRect(hWnd, out RECT from))
                    moves.Add((hWnd, from, posX[i], posY[i], w[i], h[i]));
                else
                    NativeMethods.MoveWindow(hWnd, posX[i], posY[i], w[i], h[i], true);
            }
            catch { }
        }

        if (!animate || moves.Count == 0) return;
        AnimateMoves(moves);
    }

    // Ease each window from its current rect to its target over ~220ms so focus
    // reshuffles (promote / carousel) glide instead of snapping. Repaints are
    // deferred during the slide and forced once on the final frame to stay smooth.
    private static void AnimateMoves(List<(IntPtr hWnd, RECT from, int tx, int ty, int tw, int th)> moves)
    {
        _moveAnimTimer?.Stop();
        const double durationMs = 275.0;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(15) };
        timer.Tick += (_, _) =>
        {
            double t = Math.Min(1.0, clock.Elapsed.TotalMilliseconds / durationMs);
            // easeInOutQuad
            double e = t < 0.5 ? 2 * t * t : 1 - Math.Pow(-2 * t + 2, 2) / 2;
            bool last = t >= 1.0;
            foreach (var m in moves)
            {
                int fw = m.from.Right - m.from.Left;
                int fh = m.from.Bottom - m.from.Top;
                int x = last ? m.tx : (int)(m.from.Left + (m.tx - m.from.Left) * e);
                int y = last ? m.ty : (int)(m.from.Top + (m.ty - m.from.Top) * e);
                int ww = last ? m.tw : (int)(fw + (m.tw - fw) * e);
                int hh = last ? m.th : (int)(fh + (m.th - fh) * e);
                NativeMethods.MoveWindow(m.hWnd, x, y, ww, hh, last);
            }
            if (last)
            {
                timer.Stop();
                _moveAnimTimer = null;
            }
        };
        _moveAnimTimer = timer;
        timer.Start();
    }

    private void OnCloseQuadClick(object sender, MouseButtonEventArgs e)
    {
        QuadLauncher.StopDevServer(_serverPort);
        QuadLauncher.CloseTerminal(_trackedHWnd);
    }

    private void OnCloseAllClick(object sender, MouseButtonEventArgs e)
    {
        var config = QuadConfig.Load();
        var ports = config?.DevServerPorts ?? [3000, 3001, 3002, 3003];
        foreach (var port in ports)
            QuadLauncher.StopDevServer(port);

        var appDataDir = PathHelper.AppDataDir;

        for (int i = 0; i < 100; i++)
        {
            var hwndFile = Path.Combine(appDataDir, $"quad-{i}.hwnd");
            if (!File.Exists(hwndFile))
            {
                if (i >= 5) break;
                continue;
            }

            try
            {
                var hwndVal = long.Parse(File.ReadAllText(hwndFile).Trim());
                var hWnd = new IntPtr(hwndVal);
                if (NativeMethods.IsWindow(hWnd))
                    QuadLauncher.CloseTerminal(hWnd, force: true);
            }
            catch { }
        }
    }

    // ────────────────────────────────────────────────────────────
    //  Keyboard handling
    // ────────────────────────────────────────────────────────────

    private void OnLabelKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Escape)
        {
            NativeMethods.SetForegroundWindow(_trackedHWnd);
            e.Handled = true;
        }
    }

    // ────────────────────────────────────────────────────────────
    //  Dev server
    // ────────────────────────────────────────────────────────────

    private void InitServerPanel()
    {
        var config = QuadConfig.Load();
        var ports = config?.DevServerPorts ?? [3000, 3001, 3002, 3003];
        _serverPort = _quadIndex >= 0 && _quadIndex < ports.Length ? ports[_quadIndex] : 3000 + _quadIndex;
        ServerLink.Text = $":{_serverPort}";
        UpdateServerDot();
    }

    private bool IsPortListening(int port)
    {
        try
        {
            var listeners = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners();
            return listeners.Any(ep => ep.Port == port);
        }
        catch { return false; }
    }

    private void UpdateServerDot()
    {
        bool running = IsPortListening(_serverPort);
        if (running == _serverRunning) return;
        _serverRunning = running;
        ServerDot.Fill = new SolidColorBrush(running
            ? (Color)ColorConverter.ConvertFromString("#00CC77")
            : (Color)ColorConverter.ConvertFromString("#555555"));
        ServerLink.Foreground = new SolidColorBrush(running
            ? (Color)ColorConverter.ConvertFromString("#00CCFF")
            : (Color)ColorConverter.ConvertFromString("#6699CC"));
    }

    private void OnServerLinkClick(object sender, MouseButtonEventArgs e)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = $"http://localhost:{_serverPort}",
            UseShellExecute = true
        });
    }

    private void OnServerStartClick(object sender, MouseButtonEventArgs e)
    {
        if (_serverRunning) return;

        var config = QuadConfig.Load();
        // Repo-local .quadclaude.json may override the command (e.g. yarn dev / pnpm dev)
        var command = config != null
            ? QuadLauncher.ResolveDevServerCommand(config, _quadIndex, _serverPort)
            : $"npm run dev -- --port {_serverPort}";

        QuadLauncher.StartDevServerTab(_quadIndex, command, config, _lastCwd);
    }

    private void OnServerStopClick(object sender, MouseButtonEventArgs e)
    {
        QuadLauncher.StopDevServer(_serverPort);
    }

    // ────────────────────────────────────────────────────────────
    //  Label visibility
    // ────────────────────────────────────────────────────────────

    private void OnShowLabelChanged(object sender, RoutedEventArgs e)
    {
        var border = LabelBox.Parent as FrameworkElement;
        if (border != null)
            border.Visibility = ShowLabelCheck.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        SaveState();
    }

    private void OnShowExplorerChanged(object sender, RoutedEventArgs e)
    {
        ExplorerBtn.Visibility = ShowExplorerCheck.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        SaveState();
    }

    private void OnShowPhaseChanged(object sender, RoutedEventArgs e)
    {
        PhaseBtn.Visibility = ShowPhaseCheck.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        SaveState();
    }

    private void OnShowPasteChanged(object sender, RoutedEventArgs e)
    {
        PasteImageBtn.Visibility = ShowPasteCheck.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        SaveState();
    }

    private void OnIdleOverlayChanged(object sender, RoutedEventArgs e)
    {
        if (!_initialized) return;
        var config = QuadConfig.Load() ?? new QuadConfig();
        config.IdleOverlayEnabled = IdleOverlayCheck.IsChecked == true;
        config.Save();
    }

    private void OnPartyModeChanged(object sender, RoutedEventArgs e)
    {
        if (!_initialized) return;
        var config = QuadConfig.Load() ?? new QuadConfig();
        config.PartyModeEnabled = PartyModeCheck.IsChecked == true;
        config.Save();
    }

    private void OnCarouselChanged(object sender, RoutedEventArgs e)
    {
        if (!_initialized) return;
        var config = QuadConfig.Load() ?? new QuadConfig();
        config.CarouselModeEnabled = CarouselCheck.IsChecked == true;
        config.Save();
    }

    private void OnIdleTintChanged(object sender, RoutedEventArgs e)
    {
        if (!_initialized) return;
        var config = QuadConfig.Load() ?? new QuadConfig();
        config.IdleTintEnabled = IdleTintCheck.IsChecked == true;
        config.Save();
        RefreshIdleOverlayTint(config);
    }

    private void OnIdleTintAmountChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_initialized) return;
        var config = QuadConfig.Load() ?? new QuadConfig();
        config.IdleTintAmount = IdleTintSlider.Value;
        config.Save();
        RefreshIdleOverlayTint(config);
    }

    // Re-apply tint to a visible overlay immediately so the menu controls
    // give live feedback without waiting for the next status refresh.
    private void RefreshIdleOverlayTint(QuadConfig config)
    {
        if (_idleOverlay?.Visibility != Visibility.Visible) return;
        _idleOverlay.TintEnabled = config.IdleTintEnabled;
        _idleOverlay.TintAmount = config.IdleTintAmount;
        var (_, label, colorHex) = OverlayVisualForState(GetSessionState(), config);
        _idleOverlay.SetStatus(label, colorHex);
    }

    // ────────────────────────────────────────────────────────────
    //  Glow color selector
    // ────────────────────────────────────────────────────────────

    private static readonly (string Name, string Hex)[] GlowPresets =
    [
        ("Green",  "#00FF88"),
        ("Red",    "#FF4444"),
        ("Orange", "#FFB347"),
        ("Yellow", "#FFE066"),
        ("Blue",   "#4488FF"),
        ("Teal",   "#1FE0BF"),
        ("Cyan",   "#00CCFF"),
        ("Purple", "#BB88FF"),
        ("Pink",   "#FF66AA"),
        ("White",  "#EEEEF0"),
        ("Gray",   "#666677"),
        ("Off",    "none"),
    ];

    private void OnGlowColorClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border border || border.Tag is not string glowState) return;

        var config = QuadConfig.Load() ?? new QuadConfig();

        string currentHex = glowState switch
        {
            "working" => config.GlowColorWorking,
            "done"    => config.GlowColorDone,
            _         => "#00FF88"
        };

        int idx = Array.FindIndex(GlowPresets, p => p.Hex.Equals(currentHex, StringComparison.OrdinalIgnoreCase));
        int next = (idx + 1) % GlowPresets.Length;
        string newHex = GlowPresets[next].Hex;

        switch (glowState)
        {
            case "working": config.GlowColorWorking = newHex; break;
            case "done":    config.GlowColorDone = newHex; break;
        }
        config.Save();
        UpdateGlowSwatches(config);
    }

    private void UpdateGlowSwatches(QuadConfig? config = null)
    {
        config ??= QuadConfig.Load() ?? new QuadConfig();
        try
        {
            ApplySwatch(GlowWorkingSwatch, GlowWorkingOff, config.GlowColorWorking);
            ApplySwatch(GlowDoneSwatch, GlowDoneOff, config.GlowColorDone);
        }
        catch { }
    }

    private static void ApplySwatch(System.Windows.Shapes.Ellipse swatch, TextBlock offLabel, string hex)
    {
        if (hex == "none")
        {
            swatch.Visibility = Visibility.Collapsed;
            offLabel.Visibility = Visibility.Visible;
        }
        else
        {
            swatch.Visibility = Visibility.Visible;
            offLabel.Visibility = Visibility.Collapsed;
            swatch.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        }
    }

    private void OnShowMondayChanged(object sender, RoutedEventArgs e)
    {
        MondayBtn.Visibility = ShowMondayCheck.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        SaveState();
    }

    // ────────────────────────────────────────────────────────────
    //  Image paste
    // ────────────────────────────────────────────────────────────

    private async void OnPasteImageClick(object sender, MouseButtonEventArgs e)
    {
        if (!Clipboard.ContainsImage()) return;

        var image = Clipboard.GetImage();
        if (image == null) return;

        var screenshotsDir = Path.Combine(PathHelper.AppDataDir, "screenshots");
        Directory.CreateDirectory(screenshotsDir);

        var fileName = $"screenshot-{DateTime.Now:yyyyMMdd-HHmmss-fff}.png";
        var filePath = Path.Combine(screenshotsDir, fileName);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using (var stream = File.Create(filePath))
            encoder.Save(stream);

        Clipboard.SetText(filePath);

        NativeMethods.SetForegroundWindow(_trackedHWnd);
        await Task.Delay(200);

        NativeMethods.keybd_event(NativeMethods.VK_CONTROL, 0, 0, UIntPtr.Zero);
        NativeMethods.keybd_event(NativeMethods.VK_V, 0, 0, UIntPtr.Zero);
        NativeMethods.keybd_event(NativeMethods.VK_V, 0, NativeMethods.KEYEVENTF_KEYUP, UIntPtr.Zero);
        NativeMethods.keybd_event(NativeMethods.VK_CONTROL, 0, NativeMethods.KEYEVENTF_KEYUP, UIntPtr.Zero);
    }

    // ────────────────────────────────────────────────────────────
    //  Persistence
    // ────────────────────────────────────────────────────────────

    private void LoadState()
    {
        try
        {
            if (!File.Exists(_stateFile)) return;
            var json = File.ReadAllText(_stateFile);
            var state = JsonSerializer.Deserialize<StatusState>(json);
            if (state == null) return;

            LabelBox.Text = state.Label ?? "";
            // Migrate old phase names: Active→Ready, Paused→Blocked
            var phase = state.Phase switch
            {
                "Active" => "Ready",
                "Paused" => "Blocked",
                _ => state.Phase
            };
            _phaseIndex = Array.FindIndex(Phases, p => p.Name == phase);
            if (_phaseIndex < 0) _phaseIndex = 0;
            if (state.Size != null && SizeScales.ContainsKey(state.Size))
                _sizeMode = state.Size;
            ShowLabelCheck.IsChecked = state.ShowLabel;
            var labelBorder = LabelBox.Parent as FrameworkElement;
            if (labelBorder != null)
                labelBorder.Visibility = state.ShowLabel ? Visibility.Visible : Visibility.Collapsed;
            ShowExplorerCheck.IsChecked = state.ShowExplorer;
            ExplorerBtn.Visibility = state.ShowExplorer ? Visibility.Visible : Visibility.Collapsed;
            ShowPhaseCheck.IsChecked = state.ShowPhase;
            PhaseBtn.Visibility = state.ShowPhase ? Visibility.Visible : Visibility.Collapsed;
            ShowPasteCheck.IsChecked = state.ShowPaste;
            PasteImageBtn.Visibility = state.ShowPaste ? Visibility.Visible : Visibility.Collapsed;
            ShowMondayCheck.IsChecked = state.ShowMonday;
            MondayBtn.Visibility = state.ShowMonday ? Visibility.Visible : Visibility.Collapsed;
        }
        catch { }
    }

    private void SaveState()
    {
        try
        {
            var state = new StatusState
            {
                Label = LabelBox.Text,
                Phase = Phases[_phaseIndex].Name,
                Size = _sizeMode,
                ShowLabel = ShowLabelCheck.IsChecked == true,
                ShowExplorer = ShowExplorerCheck.IsChecked == true,
                ShowPhase = ShowPhaseCheck.IsChecked == true,
                ShowPaste = ShowPasteCheck.IsChecked == true,
                ShowMonday = ShowMondayCheck.IsChecked == true
            };
            File.WriteAllText(_stateFile, JsonSerializer.Serialize(state));
        }
        catch { }
    }

    // ────────────────────────────────────────────────────────────
    //  State models
    // ────────────────────────────────────────────────────────────

    private class StatusState
    {
        public string? Label { get; set; }
        public string? Phase { get; set; }
        public string? Size { get; set; }
        public bool ShowLabel { get; set; } = true;
        public bool ShowExplorer { get; set; } = true;
        public bool ShowPhase { get; set; } = true;
        public bool ShowPaste { get; set; } = true;
        public bool ShowMonday { get; set; } = true;
    }

    private class CwdState
    {
        [System.Text.Json.Serialization.JsonPropertyName("cwd")]
        public string? Cwd { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("project")]
        public string? Project { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("branch")]
        public string? Branch { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("sessionId")]
        public string? SessionId { get; set; }
    }
}
