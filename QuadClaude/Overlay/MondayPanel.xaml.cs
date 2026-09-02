using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using QuadClaude.Config;
using QuadClaude.Interop;
using QuadClaude.Monday;

namespace QuadClaude.Overlay;

/// <summary>Status label → colored brush for the list pills / color bar.</summary>
public class StatusToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => MondayStatusColors.StatusBrush(value as string);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// A docked overlay listing monday.com action items for one quad. Click a task
/// to swap that quad's terminal into the task's Claude session. Two fetch
/// backends (GraphQL / CLI) toggle in the header so they can be compared live.
///
/// Tracks its quad by index (re-reading quad-N.hwnd each tick) rather than
/// pinning to one HWND, so it re-docks automatically when a task swap relaunches
/// the terminal. One panel per quad (singleton registry).
/// </summary>
public partial class MondayPanel : Window
{
    private static readonly Dictionary<int, MondayPanel> Instances = new();

    private readonly int _quadIndex;
    private readonly QuadConfig _config;
    private readonly DispatcherTimer _timer;
    private readonly ObservableCollection<MondayTask> _tasks = new();
    private readonly bool _standalone; // debug command: don't dock / don't auto-close
    private int _missingTicks;
    private bool _fetching;

    private static string AppDataDir => PathHelper.AppDataDir;

    /// <summary>Open the panel for a quad, or close it if already open (toggle).</summary>
    public static void Toggle(int quadIndex, QuadConfig config)
    {
        if (Instances.TryGetValue(quadIndex, out var existing))
        {
            existing.Close();
            return;
        }
        var panel = new MondayPanel(quadIndex, config);
        Instances[quadIndex] = panel;
        panel.Show();
    }

    public MondayPanel(int quadIndex, QuadConfig config, bool standalone = false)
    {
        InitializeComponent();
        _quadIndex = quadIndex;
        _config = config;
        _standalone = standalone;

        TaskList.ItemsSource = _tasks;
        if (_standalone) PositionStandalone();
        else PositionToCurrentTerminal();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _timer.Tick += OnTimerTick;

        Loaded += OnLoaded;
        Closed += (_, _) =>
        {
            _timer.Stop();
            Instances.Remove(_quadIndex);
        };
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var helper = new WindowInteropHelper(this);
        NativeMethods.HideFromTaskView(helper.Handle);
        _timer.Start();
        _ = FetchAsync();
    }

    // ── positioning (track quad by index, survive relaunches) ──────

    private void OnTimerTick(object? sender, EventArgs e)
    {
        if (_standalone) return; // debug mode: fixed position, never auto-close

        var hWnd = QuadLauncher.ReadQuadHWnd(_quadIndex);
        if (hWnd == IntPtr.Zero)
        {
            // Terminal momentarily gone (e.g. mid task-swap). Tolerate a gap;
            // only give up if it stays gone (~10s) — then the quad is really closed.
            if (++_missingTicks > 20) Close();
            return;
        }
        _missingTicks = 0;
        PositionPanel(hWnd);
    }

    private void PositionStandalone()
    {
        var wa = SystemParameters.WorkArea;
        Height = Math.Min(700, wa.Height - 40);
        Left = wa.Right - Width - 20;
        Top = wa.Top + 20;
    }

    private void PositionToCurrentTerminal()
    {
        var hWnd = QuadLauncher.ReadQuadHWnd(_quadIndex);
        if (hWnd != IntPtr.Zero) PositionPanel(hWnd);
    }

    private void PositionPanel(IntPtr hWnd)
    {
        if (!NativeMethods.GetWindowRect(hWnd, out RECT rect)) return;
        var scale = NativeMethods.GetDpiScale(hWnd);
        double top = rect.Top / scale;
        double right = rect.Right / scale;
        double termHeight = (rect.Bottom - rect.Top) / scale;

        const double toolbarHeight = 44; // leave room for the StatusWidget toolbar
        Left = right - Width;
        Top = top;
        Height = Math.Max(120, termHeight - toolbarHeight);
    }

    // ── fetch ──────────────────────────────────────────────────────

    private IMondayTaskSource BuildSource() => new GraphQlTaskSource(_config);

    private async Task FetchAsync()
    {
        if (_fetching) return;
        _fetching = true;
        var source = BuildSource();
        StatusLine.Text = $"Loading via {source.Name}…";
        StatusLine.Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0xAA));

        try
        {
            var tasks = await Task.Run(() => source.FetchAsync());
            _tasks.Clear();
            foreach (var t in tasks) _tasks.Add(t);
            MarkSessionFlags();
            StatusLine.Text = $"{_tasks.Count} tasks · {source.Name}";
            StatusLine.Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0xCC, 0x88));
        }
        catch (Exception ex)
        {
            StatusLine.Text = $"{source.Name} error: {ex.Message}";
            StatusLine.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x77, 0x66));
        }
        finally
        {
            _fetching = false;
        }
    }

    // ── expand / collapse subtasks ─────────────────────────────────

    private void OnRowClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is MondayTask task && task.HasSubtasks)
            task.IsExpanded = !task.IsExpanded;
        // don't set e.Handled — selection + double-click-to-open still work
    }

    // ── cycle / refresh ────────────────────────────────────────────

    private void OnPrevClick(object sender, MouseButtonEventArgs e)
    {
        if (_tasks.Count == 0) return;
        TaskList.SelectedIndex = (TaskList.SelectedIndex - 1 + _tasks.Count) % _tasks.Count;
        TaskList.ScrollIntoView(TaskList.SelectedItem);
    }

    private void OnNextClick(object sender, MouseButtonEventArgs e)
    {
        if (_tasks.Count == 0) return;
        TaskList.SelectedIndex = (TaskList.SelectedIndex + 1) % _tasks.Count;
        TaskList.ScrollIntoView(TaskList.SelectedItem);
    }

    private void OnRefreshClick(object sender, MouseButtonEventArgs e) => _ = FetchAsync();

    private void OnCloseClick(object sender, MouseButtonEventArgs e) => Close();

    /// <summary>Flag tasks/subtasks that have a real local session (drives the ● dot).</summary>
    private void MarkSessionFlags()
    {
        var map = MondaySessionMap.Load();
        var live = AllSessionIds(); // one fs scan; a mapped pulse only counts if its transcript exists
        bool Has(string pulseId) => map.ByPulse.TryGetValue(pulseId, out var e) && live.Contains(e.SessionId);

        foreach (var t in _tasks)
        {
            t.HasSession = Has(t.Id);
            foreach (var s in t.Subtasks) s.HasSession = Has(s.Id);
        }
        TaskList.Items.Refresh();
    }

    // ── per-task context menu (open in browser / forget session) ───

    private void OnRowRightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not MondayTask task) return;

        var menu = new ContextMenu
        {
            Background = new SolidColorBrush(Color.FromArgb(0xEE, 0x1E, 0x1E, 0x2E)),
            Foreground = new SolidColorBrush(Color.FromRgb(0xEE, 0xEE, 0xF0)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
        };

        var openWeb = new MenuItem { Header = "Open on monday.com" };
        openWeb.Click += (_, _) =>
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(task.Url) { UseShellExecute = true }); }
            catch { }
        };
        menu.Items.Add(openWeb);

        var forget = new MenuItem
        {
            Header = "Forget session (start fresh next time)",
            IsEnabled = task.HasSession,
        };
        forget.Click += (_, _) =>
        {
            var map = MondaySessionMap.Load();
            if (map.ByPulse.Remove(task.Id))
            {
                map.Save();
                task.HasSession = false;
                TaskList.Items.Refresh();
                StatusLine.Text = $"Forgot session: {task.DisplayName}";
                StatusLine.Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xDD));
            }
        };
        menu.Items.Add(forget);

        fe.ContextMenu = menu;
        menu.IsOpen = true;
        e.Handled = true;
    }

    // ── open a task or subtask as a Claude session in this quad ────

    private void OnListDoubleClick(object sender, MouseButtonEventArgs e) => OpenSelected();
    private void OnOpenClick(object sender, MouseButtonEventArgs e) => OpenSelected();

    private void OpenSelected()
    {
        if (TaskList.SelectedItem is MondayTask t)
            _ = OpenItemAsync(t.Id, t.Name, t.Branch, t.Url, t.Status, t.Priority, t.Owner);
    }

    /// <summary>
    /// New session → run the picker, seed Claude with the item brief.
    /// Resume → return to the session's own home dir (skip the picker) so
    /// `claude --resume` can find the conversation.
    /// </summary>
    private async Task OpenItemAsync(string pulseId, string name, string? branch, string url,
        string? status, string? priority, string? owner)
    {
        var map = MondaySessionMap.Load();
        var quadCwd = ResolveQuadCwd() ?? (_config.ProjectsDir is { Length: > 0 } pd ? pd : null);
        var entry = map.GetOrCreate(pulseId, quadCwd, branch, name);

        bool resume = entry.Started && SessionTranscriptExists(entry.SessionId);
        if (!resume) entry.Started = false;

        string? launchDir;
        string? prompt = null;
        if (resume)
        {
            launchDir = SessionTranscriptCwd(entry.SessionId) ?? entry.Cwd;
        }
        else
        {
            launchDir = null; // picker decides the worktree for a fresh session
            StatusLine.Text = $"Loading detail: {name}";
            StatusLine.Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0xAA));
            try
            {
                var detail = await Task.Run(() => BuildSource().FetchDetailAsync(pulseId));
                prompt = MondayPrompt.Build(name, url, status, priority, owner, branch, detail);
            }
            catch
            {
                prompt = MondayPrompt.Build(name, url, status, priority, owner, branch, MondayTaskDetail.Empty);
            }
        }

        StatusLine.Text = $"{(resume ? "Resuming" : "Starting")}: {name}";
        StatusLine.Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xDD));

        var qi = _quadIndex;
        var cfg = _config;
        var sessionId = entry.SessionId;
        var br = entry.Branch;
        var dir = launchDir;

        var thread = new Thread(() =>
        {
            QuadLauncher.LaunchTaskInQuad(qi, dir, br, sessionId, resume, prompt, cfg);
            entry.Started = true;
            map.Save();
            Dispatcher.Invoke(MarkSessionFlags);
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    /// <summary>True if a transcript for this session id exists under ~/.claude/projects.</summary>
    private static bool SessionTranscriptExists(string sessionId)
    {
        try
        {
            var projects = ClaudeProjectsDir();
            return projects != null
                && Directory.EnumerateFiles(projects, sessionId + ".jsonl", SearchOption.AllDirectories).Any();
        }
        catch { return false; }
    }

    /// <summary>All session ids that have a transcript on disk (single scan).</summary>
    private static HashSet<string> AllSessionIds()
    {
        var set = new HashSet<string>();
        try
        {
            var projects = ClaudeProjectsDir();
            if (projects != null)
                foreach (var f in Directory.EnumerateFiles(projects, "*.jsonl", SearchOption.AllDirectories))
                    set.Add(Path.GetFileNameWithoutExtension(f));
        }
        catch { }
        return set;
    }

    /// <summary>The cwd a session was created in, read from its transcript's first record.</summary>
    private static string? SessionTranscriptCwd(string sessionId)
    {
        try
        {
            var projects = ClaudeProjectsDir();
            if (projects == null) return null;
            var file = Directory.EnumerateFiles(projects, sessionId + ".jsonl", SearchOption.AllDirectories).FirstOrDefault();
            if (file == null) return null;
            foreach (var line in File.ReadLines(file))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                using var doc = JsonDocument.Parse(line);
                if (doc.RootElement.TryGetProperty("cwd", out var c) && c.GetString() is { Length: > 0 } cwd)
                    return cwd.Replace('/', '\\');
            }
        }
        catch { }
        return null;
    }

    private static string? ClaudeProjectsDir()
    {
        var p = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "projects");
        return Directory.Exists(p) ? p : null;
    }

    private void OnSubtaskMouseDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true; // isolate from the parent card's expand toggle / selection
        if (e.ClickCount == 2 && sender is FrameworkElement fe && fe.DataContext is MondaySubtask sub)
            _ = OpenItemAsync(sub.Id, sub.Name, null, sub.Url, sub.Status, null, null);
    }

    private void OnSubtaskMouseUp(object sender, MouseButtonEventArgs e) => e.Handled = true;

    private void OnSubtaskRightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not MondaySubtask sub) return;
        e.Handled = true;

        var menu = NewDarkMenu();
        var open = new MenuItem { Header = sub.HasSession ? "Resume this subtask's session" : "Open as its own session" };
        open.Click += (_, _) => _ = OpenItemAsync(sub.Id, sub.Name, null, sub.Url, sub.Status, null, null);
        menu.Items.Add(open);

        var web = new MenuItem { Header = "Open on monday.com" };
        web.Click += (_, _) => OpenUrl(sub.Url);
        menu.Items.Add(web);

        fe.ContextMenu = menu;
        menu.IsOpen = true;
    }

    private static ContextMenu NewDarkMenu() => new()
    {
        Background = new SolidColorBrush(Color.FromArgb(0xEE, 0x1E, 0x1E, 0x2E)),
        Foreground = new SolidColorBrush(Color.FromRgb(0xEE, 0xEE, 0xF0)),
        BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
    };

    private static void OpenUrl(string url)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { }
    }

    /// <summary>Read the quad's currently tracked cwd (Windows form), if any.</summary>
    private string? ResolveQuadCwd()
    {
        try
        {
            var file = Path.Combine(AppDataDir, $"quad-{_quadIndex}.cwd.json");
            if (!File.Exists(file)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            if (doc.RootElement.TryGetProperty("cwd", out var cwdProp))
            {
                var cwd = cwdProp.GetString();
                if (string.IsNullOrWhiteSpace(cwd)) return null;
                // Normalize MSYS (/c/...) → Windows (C:\...)
                if (cwd.Length >= 3 && cwd[0] == '/' && cwd[2] == '/')
                    cwd = $"{char.ToUpper(cwd[1])}:{cwd[2..]}";
                return cwd.Replace('/', '\\');
            }
        }
        catch { }
        return null;
    }
}
