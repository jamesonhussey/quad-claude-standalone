using System.ComponentModel;

namespace QuadClaude.Monday;

/// <summary>A subitem (subtask) of a board item — can spawn its own session.</summary>
public record MondaySubtask(string Id, string Name, string? Status, string? StatusColor, string Url)
    : INotifyPropertyChanged
{
    public string DisplayName => Name.Length > 64 ? Name[..61] + "…" : Name;

    private bool _hasSession;
    public bool HasSession
    {
        get => _hasSession;
        set { if (_hasSession != value) { _hasSession = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasSession))); } }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>
/// A single action item from a monday.com board, normalized to what the panel
/// needs. Implements change notification so expand state + the session dot
/// update the UI reactively.
/// </summary>
public record MondayTask(
    string Id,           // monday pulse id
    string Name,         // item title
    string? Status,      // Status column label (e.g. "Working on it")
    string? StatusColor, // hex color for the status pill
    string? Priority,    // Priority column label (e.g. "High")
    string? Branch,      // Git branch column (text_mm1t4h16), may be empty
    string? Owner,       // owner display name
    string? Group,       // group title (e.g. "Current Sprint")
    string Url)          // direct link to the item on monday.com
    : INotifyPropertyChanged
{
    /// <summary>A short, single-line label for the list row.</summary>
    public string DisplayName => Name.Length > 60 ? Name[..57] + "…" : Name;

    /// <summary>Subtasks, populated at fetch time.</summary>
    public List<MondaySubtask> Subtasks { get; set; } = new();

    public bool HasSubtasks => Subtasks.Count > 0;
    public string SubtaskCountLabel => Subtasks.Count > 0 ? $"{Subtasks.Count}" : "";

    private bool _hasSession;
    /// <summary>True when this pulse already has a local Claude session (drives the ● dot).</summary>
    public bool HasSession
    {
        get => _hasSession;
        set { if (_hasSession != value) { _hasSession = value; OnPropertyChanged(nameof(HasSession)); } }
    }

    private bool _isExpanded;
    /// <summary>Whether the subtask dropdown is open.</summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set { if (_isExpanded != value) { _isExpanded = value; OnPropertyChanged(nameof(IsExpanded)); OnPropertyChanged(nameof(CaretGlyph)); } }
    }

    /// <summary>▸ collapsed, ▾ expanded, blank when there are no subtasks.</summary>
    public string CaretGlyph => !HasSubtasks ? "" : (IsExpanded ? "▾" : "▸");

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string name)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
