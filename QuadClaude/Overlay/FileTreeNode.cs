using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;

namespace QuadClaude.Overlay;

public class FileTreeNode : INotifyPropertyChanged
{
    private static readonly HashSet<string> ExcludedDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", "node_modules", "bin", "obj", ".vs", "__pycache__",
        ".next", "dist", "coverage", ".cache", ".nuget", "packages",
        ".terraform", "vendor", "target", "build", ".idea"
    };

    public static bool IsExcludedDir(string name)
        => ExcludedDirs.Contains(name) || name.StartsWith('.');

    private bool _isExpanded;
    private bool _isLoaded;

    public string Name { get; }
    public string FullPath { get; }
    public bool IsDirectory { get; }
    public ObservableCollection<FileTreeNode> Children { get; } = [];

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value) return;
            _isExpanded = value;
            OnPropertyChanged();

            if (value && !_isLoaded && IsDirectory)
                LoadChildren();
        }
    }

    public string Icon => IsDirectory
        ? (_isExpanded ? "\uD83D\uDCC2" : "\uD83D\uDCC1")
        : GetFileIcon(Name);

    public FileTreeNode(string fullPath, bool isDirectory)
    {
        FullPath = fullPath;
        Name = Path.GetFileName(fullPath);
        IsDirectory = isDirectory;

        // Add a placeholder child so the expander arrow shows
        if (isDirectory)
            Children.Add(CreatePlaceholder());
    }

    private static FileTreeNode CreatePlaceholder()
    {
        return new FileTreeNode("", false)
        {
            _isLoaded = true
        };
    }

    public void LoadChildren()
    {
        if (_isLoaded) return;
        _isLoaded = true;

        Children.Clear();

        try
        {
            var entries = new List<FileTreeNode>();

            foreach (var dir in Directory.EnumerateDirectories(FullPath))
            {
                var dirName = Path.GetFileName(dir);
                if (ExcludedDirs.Contains(dirName)) continue;
                if (dirName.StartsWith('.')) continue; // skip hidden dirs
                entries.Add(new FileTreeNode(dir, true));
            }

            foreach (var file in Directory.EnumerateFiles(FullPath))
            {
                entries.Add(new FileTreeNode(file, false));
            }

            // Sort: folders first, then alphabetical
            entries.Sort((a, b) =>
            {
                if (a.IsDirectory != b.IsDirectory)
                    return a.IsDirectory ? -1 : 1;
                return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });

            foreach (var entry in entries)
                Children.Add(entry);
        }
        catch
        {
            // Permission denied or path too long — show empty
        }
    }

    public void Refresh()
    {
        if (!_isLoaded || !IsDirectory) return;
        _isLoaded = false;
        var wasExpanded = _isExpanded;
        Children.Clear();
        if (wasExpanded)
        {
            Children.Add(new FileTreeNode("Loading...", false) { _isLoaded = true });
            _isLoaded = false;
            LoadChildren();
        }
        else
        {
            Children.Add(new FileTreeNode("Loading...", false) { _isLoaded = true });
        }
    }

    private static string GetFileIcon(string name)
    {
        var ext = Path.GetExtension(name).ToLowerInvariant();
        return ext switch
        {
            ".cs" => "\u2660",      // spade — C#
            ".ts" or ".tsx" => "\u25C6", // diamond — TypeScript
            ".js" or ".jsx" => "\u25CB", // circle — JavaScript
            ".json" => "{ }",
            ".md" => "\u00B6",      // pilcrow — markdown
            ".sh" or ".bat" => ">_",
            ".xaml" or ".xml" or ".html" => "</>",
            ".css" or ".scss" => "#",
            ".png" or ".jpg" or ".gif" or ".svg" or ".ico" => "\u25A3", // square with fill
            ".sln" or ".csproj" => "\u2726", // four-pointed star
            _ => "\u2022"           // bullet
        };
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        if (name == nameof(IsExpanded))
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Icon)));
    }
}
