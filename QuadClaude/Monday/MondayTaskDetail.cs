namespace QuadClaude.Monday;

/// <summary>One update/comment on a monday.com item.</summary>
public record MondayUpdate(string? Author, string? Date, string Text);

/// <summary>
/// The full body of an item — fetched on demand when a task is opened (not in
/// the list query, which stays lightweight). Feeds the seed prompt.
/// </summary>
public record MondayTaskDetail(
    string? Description,          // plain text extracted from the item's description blocks
    List<MondayUpdate> Updates)   // most-recent updates, newest first
{
    public static MondayTaskDetail Empty => new(null, new List<MondayUpdate>());
}
