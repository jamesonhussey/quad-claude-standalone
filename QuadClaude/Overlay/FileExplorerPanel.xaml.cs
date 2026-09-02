using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using QuadClaude.Interop;

namespace QuadClaude.Overlay;

/// <summary>
/// Converter: directories get a warm folder color, files get muted gray.
/// </summary>
public class DirColorConverter : IValueConverter
{
    private static readonly SolidColorBrush DirBrush = new(Color.FromRgb(0xDD, 0xAA, 0x44));
    private static readonly SolidColorBrush FileBrush = new(Color.FromRgb(0x99, 0x9A, 0xAA));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? DirBrush : FileBrush;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public partial class FileExplorerPanel : Window
{
    private readonly IntPtr _trackedHWnd;
    private readonly int _quadIndex;
    private readonly Window _statusWidget; // to read its actual height
    private string _rootPath;
    private readonly DispatcherTimer _timer;
    private readonly string _cwdFile;
    private readonly string _projectRoot; // ceiling — can't navigate above this
    private string _lastCwdJson = "";
    private int _cwdPollCounter;

    public ObservableCollection<FileTreeNode> RootNodes { get; } = [];

    public FileExplorerPanel(IntPtr trackedHWnd, int quadIndex, string rootPath, Window statusWidget)
    {
        InitializeComponent();

        _trackedHWnd = trackedHWnd;
        _quadIndex = quadIndex;
        _rootPath = rootPath;
        _statusWidget = statusWidget;

        var appDataDir = QuadClaude.Config.PathHelper.AppDataDir;
        _cwdFile = Path.Combine(appDataDir, quadIndex >= 0
            ? $"quad-{quadIndex}.cwd.json"
            : "quad-default.cwd.json");

        // The project root's parent is the ceiling for "go up"
        _projectRoot = Path.GetDirectoryName(rootPath) ?? rootPath;

        HeaderText.Text = Path.GetFileName(rootPath);
        HeaderText.ToolTip = rootPath;

        FileTree.ItemsSource = RootNodes;
        // Pre-read the cwd json so the first poll doesn't trigger a spurious reload
        try { if (File.Exists(_cwdFile)) _lastCwdJson = File.ReadAllText(_cwdFile); } catch { }

        LoadTree(rootPath);
        PositionPanel();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _timer.Tick += OnTimerTick;

        Loaded += OnLoaded;
        Closed += (_, _) => _timer.Stop();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var helper = new WindowInteropHelper(this);
        NativeMethods.HideFromTaskView(helper.Handle);
        _timer.Start();
    }

    // ────────────────────────────────────────────────────────────
    //  Positioning
    // ────────────────────────────────────────────────────────────

    private void OnTimerTick(object? sender, EventArgs e)
    {
        if (!NativeMethods.IsWindow(_trackedHWnd))
        {
            _timer.Stop();
            Close();
            return;
        }

        PositionPanel();

        // Poll cwd every ~2 seconds
        _cwdPollCounter++;
        if (_cwdPollCounter >= 4)
        {
            _cwdPollCounter = 0;
            CheckCwdChange();
        }
    }

    private void PositionPanel()
    {
        if (!NativeMethods.GetWindowRect(_trackedHWnd, out RECT rect)) return;

        var scale = NativeMethods.GetDpiScale(_trackedHWnd);
        double left = rect.Left / scale;
        double top = rect.Top / scale;
        double termHeight = (rect.Bottom - rect.Top) / scale;

        double right = rect.Right / scale;

        // Reserve space at the bottom for the StatusWidget toolbar
        double toolbarHeight = _statusWidget.ActualHeight > 0 ? _statusWidget.ActualHeight + 4 : 40;

        // Dock to the right edge of the terminal, above the toolbar
        Left = right - Width;
        Top = top;
        Height = Math.Max(100, termHeight - toolbarHeight);
    }

    private void PlaceAboveTerminal(IntPtr panelHWnd)
    {
        // Keep the file explorer in front of the terminal by placing
        // the terminal behind the panel. Only do this when the terminal
        // is the foreground window (user is interacting with this quad).
        var fg = NativeMethods.GetForegroundWindow();
        if (fg == _trackedHWnd || fg == panelHWnd)
        {
            // Ensure panel is above terminal: set terminal's z-order to behind panel
            NativeMethods.SetWindowPos(
                panelHWnd, IntPtr.Zero, // HWND_TOP
                0, 0, 0, 0,
                NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOACTIVATE);
        }
    }

    // ────────────────────────────────────────────────────────────
    //  Tree loading
    // ────────────────────────────────────────────────────────────

    private void LoadTree(string rootPath)
    {
        _rootPath = rootPath;
        RootNodes.Clear();

        if (!Directory.Exists(rootPath)) return;

        var rootNode = new FileTreeNode(rootPath, true);
        rootNode.IsExpanded = true; // auto-expand root
        RootNodes.Add(rootNode);
    }

    private void CheckCwdChange()
    {
        try
        {
            if (!File.Exists(_cwdFile)) return;
            var json = File.ReadAllText(_cwdFile);
            if (json == _lastCwdJson) return;
            _lastCwdJson = json;

            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("cwd", out var cwdProp))
            {
                var newCwd = cwdProp.GetString();
                if (string.IsNullOrEmpty(newCwd)) return;

                // Normalize MSYS paths (/c/Projects/...) to Windows paths
                var normalized = NormalizePath(newCwd);

                // Only auto-switch if the cwd changed to a completely different project
                // (don't jump around if user navigated with the up button)
                if (!normalized.StartsWith(_rootPath, StringComparison.OrdinalIgnoreCase)
                    && !_rootPath.StartsWith(normalized, StringComparison.OrdinalIgnoreCase))
                {
                    if (Directory.Exists(normalized))
                    {
                        HeaderText.Text = Path.GetFileName(normalized);
                        HeaderText.ToolTip = normalized;
                        LoadTree(normalized);
                    }
                }
            }
        }
        catch { }
    }

    private static string NormalizePath(string path)
    {
        // Convert MSYS paths: /c/Projects → C:\Projects
        if (path.Length >= 3 && path[0] == '/' && path[2] == '/')
        {
            path = $"{char.ToUpper(path[1])}:{path[2..]}";
        }
        return path.Replace('/', '\\');
    }

    // ────────────────────────────────────────────────────────────
    //  Search
    // ────────────────────────────────────────────────────────────

    private DispatcherTimer? _searchDebounce;

    private void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        _searchDebounce?.Stop();
        _searchDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _searchDebounce.Tick += (_, _) =>
        {
            _searchDebounce.Stop();
            ApplySearch(SearchBox.Text.Trim());
        };
        _searchDebounce.Start();
    }

    private void ApplySearch(string query)
    {
        if (string.IsNullOrEmpty(query))
        {
            // Restore normal tree
            LoadTree(_rootPath);
            return;
        }

        // Flatten and filter — show matching files across the project
        RootNodes.Clear();
        try
        {
            var matches = Directory.EnumerateFiles(_rootPath, "*", new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                MaxRecursionDepth = 8
            })
            .Where(f =>
            {
                var name = Path.GetFileName(f);
                // Skip excluded directories in path
                var rel = Path.GetRelativePath(_rootPath, f);
                var parts = rel.Split(Path.DirectorySeparatorChar);
                if (parts.Any(p => FileTreeNode.IsExcludedDir(p)))
                    return false;
                return name.Contains(query, StringComparison.OrdinalIgnoreCase);
            })
            .Take(50); // cap results

            foreach (var file in matches)
            {
                var rel = Path.GetRelativePath(_rootPath, file);
                var node = new FileTreeNode(file, false);
                RootNodes.Add(node);
            }
        }
        catch { }
    }

    // ────────────────────────────────────────────────────────────
    //  Interactions
    // ────────────────────────────────────────────────────────────

    private void OnCloseClick(object sender, MouseButtonEventArgs e)
    {
        Close();
    }

    private void OnGoUpClick(object sender, MouseButtonEventArgs e)
    {
        var parent = Path.GetDirectoryName(_rootPath);
        if (parent == null) return;

        // Don't go above the project root's parent
        if (parent.Length < _projectRoot.Length) return;

        HeaderText.Text = Path.GetFileName(parent);
        if (string.IsNullOrEmpty(HeaderText.Text))
            HeaderText.Text = parent; // drive root like C:\
        HeaderText.ToolTip = parent;
        LoadTree(parent);
    }

    private Point _dragStartPoint;
    private bool _isDragging;

    private void OnNodeMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount >= 2)
        {
            // Double-click: open file in default app
            if (sender is FrameworkElement fe && fe.Tag is FileTreeNode node && !node.IsDirectory)
            {
                try { Process.Start(new ProcessStartInfo(node.FullPath) { UseShellExecute = true }); }
                catch { }
            }
            e.Handled = true;
            return;
        }

        // Record start point for drag detection
        _dragStartPoint = e.GetPosition(this);
        _isDragging = false;
    }

    private void OnNodeMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        if (_isDragging) return;
        if (sender is not FrameworkElement fe || fe.Tag is not FileTreeNode node) return;
        if (string.IsNullOrEmpty(node.FullPath)) return;

        var pos = e.GetPosition(this);
        var diff = pos - _dragStartPoint;

        // Only start drag after moving a minimum distance (avoids accidental drags)
        if (Math.Abs(diff.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(diff.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        _isDragging = true;

        // Drag the file path as text — drops into terminal as the path string
        var data = new DataObject(DataFormats.Text, node.FullPath);
        DragDrop.DoDragDrop(fe, data, DragDropEffects.Copy);
    }

    private void OnNodeRightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.Tag is not FileTreeNode node) return;
        if (string.IsNullOrEmpty(node.FullPath)) return;

        var menu = new ContextMenu();

        var copyPath = new MenuItem { Header = "Copy Path" };
        copyPath.Click += (_, _) => Clipboard.SetText(node.FullPath);
        menu.Items.Add(copyPath);

        var copyRelPath = new MenuItem { Header = "Copy Relative Path" };
        copyRelPath.Click += (_, _) =>
            Clipboard.SetText(Path.GetRelativePath(_rootPath, node.FullPath));
        menu.Items.Add(copyRelPath);

        menu.Items.Add(new Separator());

        if (!node.IsDirectory)
        {
            var openFile = new MenuItem { Header = "Open File" };
            openFile.Click += (_, _) =>
            {
                try { Process.Start(new ProcessStartInfo(node.FullPath) { UseShellExecute = true }); }
                catch { }
            };
            menu.Items.Add(openFile);
        }

        var openExplorer = new MenuItem { Header = "Open in Explorer" };
        openExplorer.Click += (_, _) =>
        {
            try
            {
                if (node.IsDirectory)
                    Process.Start("explorer.exe", $"\"{node.FullPath}\"");
                else
                    Process.Start("explorer.exe", $"/select,\"{node.FullPath}\"");
            }
            catch { }
        };
        menu.Items.Add(openExplorer);

        // Style the context menu dark
        menu.Background = new SolidColorBrush(Color.FromArgb(0xEE, 0x1E, 0x1E, 0x2E));
        menu.Foreground = new SolidColorBrush(Color.FromRgb(0xEE, 0xEE, 0xF0));
        menu.BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF));

        fe.ContextMenu = menu;
        menu.IsOpen = true;
        e.Handled = true;
    }
}
