using System.Windows.Media;

namespace QuadClaude.Monday;

/// <summary>
/// Maps Execution Backlog status / priority labels to their monday.com hex colors,
/// so the panel can draw colored pills without querying label styles over the API.
/// Sourced from the board's column settings (get_board_info).
/// </summary>
public static class MondayStatusColors
{
    private static readonly Dictionary<string, string> Status = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Working on it"] = "#fdab3d",
        ["Done"] = "#00c875",
        ["Done for Sprint"] = "#037f4c",
        ["Stuck/Waiting"] = "#df2f4a",
        ["Self-Assigned"] = "#225091",
        ["Approved for work"] = "#74afcc",
        ["PR Submitted"] = "#9d50dd",
        ["Needs Adjustment"] = "#ff6d3b",
        ["Approved for Merging"] = "#7e3b8a",
        ["Staging Merged"] = "#9cd326",
        ["Staging Testing"] = "#ff007f",
        ["Declined"] = "#7f5347",
        ["Reviewed"] = "#401694",
        ["Rough Local"] = "#66ccff",
        ["Local Tested"] = "#579bfc",
        ["Test me: Staging"] = "#faa1f1",
        ["Staging Tested"] = "#007eb5",
        ["Prod Merged"] = "#5559df",
        ["Prod Tested"] = "#784bd1",
        ["Prod Built"] = "#bda8f9",
        ["Test me: Prod"] = "#9d99b9",
        ["Documentation"] = "#563e3e",
        ["Needs Review"] = "#216edf",
        ["Rollout"] = "#333333",
        ["Assign Effort"] = "#ffadad",
    };

    private static readonly Dictionary<string, string> Priority = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Critical ⚠️️"] = "#333333",
        ["Critical"] = "#333333",
        ["High"] = "#401694",
        ["Medium"] = "#5559df",
        ["Low"] = "#579bfc",
    };

    private const string Fallback = "#6A6A7A";

    public static string StatusHex(string? label)
        => label != null && Status.TryGetValue(label, out var hex) ? hex : Fallback;

    public static string PriorityHex(string? label)
        => label != null && Priority.TryGetValue(label, out var hex) ? hex : Fallback;

    public static SolidColorBrush StatusBrush(string? label)
        => new((Color)ColorConverter.ConvertFromString(StatusHex(label)));

    public static SolidColorBrush PriorityBrush(string? label)
        => new((Color)ColorConverter.ConvertFromString(PriorityHex(label)));
}
