namespace QuadClaude.Monday;

/// <summary>
/// Fetches action items from monday.com. Two implementations exist so the
/// panel can show them side by side:
///   • <see cref="GraphQlTaskSource"/> — C# HttpClient hits the GraphQL API directly.
///   • <see cref="CliTaskSource"/>    — shells out to a node CLI script.
/// </summary>
public interface IMondayTaskSource
{
    /// <summary>Short label shown on the panel's source toggle ("GraphQL" / "CLI").</summary>
    string Name { get; }

    /// <summary>Fetch the configured board group's items. Throws on auth/network failure.</summary>
    Task<List<MondayTask>> FetchAsync(CancellationToken ct = default);

    /// <summary>Fetch one item's full body (description + updates) for the seed prompt.</summary>
    Task<MondayTaskDetail> FetchDetailAsync(string pulseId, CancellationToken ct = default);
}
