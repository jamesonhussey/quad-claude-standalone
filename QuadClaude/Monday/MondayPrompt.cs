using System.Text;

namespace QuadClaude.Monday;

/// <summary>
/// Builds the opening message Claude receives when a task is first spawned.
/// Combines the task's columns, description, and recent updates into a brief
/// the agent can act on.
/// </summary>
public static class MondayPrompt
{
    private const int MaxUpdate = 1800;     // cap each update's length
    private const int MaxUpdatesIncluded = 3;

    public static string Build(MondayTask task, MondayTaskDetail detail)
        => Build(task.Name, task.Url, task.Status, task.Priority, task.Owner, task.Branch, detail);

    public static string Build(string title, string? url, string? status, string? priority,
        string? owner, string? branch, MondayTaskDetail detail)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You're picking up this Monday.com task. Read it, then review the relevant code and lay out a short plan before making changes.");
        sb.AppendLine();
        sb.AppendLine($"# {title}");

        var meta = new List<string>();
        if (!string.IsNullOrWhiteSpace(status)) meta.Add($"Status: {status}");
        if (!string.IsNullOrWhiteSpace(priority)) meta.Add($"Priority: {priority}");
        if (!string.IsNullOrWhiteSpace(owner)) meta.Add($"Owner: {owner}");
        if (meta.Count > 0) sb.AppendLine(string.Join(" · ", meta));
        if (!string.IsNullOrWhiteSpace(branch)) sb.AppendLine($"Branch: {branch}");
        if (!string.IsNullOrWhiteSpace(url)) sb.AppendLine($"Link: {url}");

        if (!string.IsNullOrWhiteSpace(detail.Description))
        {
            sb.AppendLine();
            sb.AppendLine("## Description");
            sb.AppendLine(detail.Description!.Trim());
        }

        if (detail.Updates.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Recent updates");
            foreach (var u in detail.Updates.Take(MaxUpdatesIncluded))
            {
                var header = u.Author is { Length: > 0 } a
                    ? (u.Date is { Length: > 0 } d ? $"[{a} · {d}]" : $"[{a}]")
                    : null;
                if (header != null) sb.AppendLine(header);
                sb.AppendLine(Truncate(u.Text, MaxUpdate));
                sb.AppendLine();
            }
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>Fallback when the detail fetch fails — seed with just the list fields.</summary>
    public static string BuildMinimal(MondayTask task)
        => Build(task, MondayTaskDetail.Empty);

    private static string Truncate(string s, int max)
        => s.Length > max ? s[..max] + "\n…(truncated)" : s;
}
