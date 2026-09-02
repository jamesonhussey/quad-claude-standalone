using System.Net.Http;
using System.Text;
using System.Text.Json;
using QuadClaude.Config;

namespace QuadClaude.Monday;

/// <summary>
/// Fetches board items by calling the monday.com GraphQL API directly from C#
/// via HttpClient. This is the "GraphQL" half of the side-by-side comparison.
/// </summary>
public class GraphQlTaskSource : IMondayTaskSource
{
    public const string Endpoint = "https://api.monday.com/v2";

    // Shared query shape (validated against the live board). The CLI script
    // (monday-tasks.mjs) sends an identical query — kept in sync intentionally.
    public const string Query =
        "query ($boardId: ID!, $groupId: String!) { " +
        "boards(ids: [$boardId]) { groups(ids: [$groupId]) { id title " +
        "items_page(limit: 100) { items { id name " +
        "column_values(ids: [\"status\",\"color_mm2c4cj6\",\"text_mm1t4h16\",\"person\"]) " +
        "{ id text } " +
        "subitems { id name column_values(ids: [\"color_mm2ej4v4\"]) { id text } } " +
        "} } } } }";

    // One item's full body — description blocks + recent updates. Fetched on
    // demand when a task is opened, to seed Claude's first message.
    public const string DetailQuery =
        "query ($itemId: [ID!]) { items(ids: $itemId) { id " +
        "description { blocks(limit: 60) { type content } } " +
        "updates(limit: 5) { text_body created_at creator { name } } } }";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    private readonly QuadConfig _config;

    public GraphQlTaskSource(QuadConfig config) => _config = config;

    public string Name => "GraphQL";

    public async Task<List<MondayTask>> FetchAsync(CancellationToken ct = default)
    {
        var body = await SendQueryAsync(Query,
            new { boardId = _config.MondayBoardId.ToString(), groupId = _config.MondayGroupId }, ct);
        return ParseResponse(body, _config.MondayHost, _config.MondayBoardId);
    }

    public async Task<MondayTaskDetail> FetchDetailAsync(string pulseId, CancellationToken ct = default)
    {
        var body = await SendQueryAsync(DetailQuery, new { itemId = new[] { pulseId } }, ct);
        return ParseDetail(body);
    }

    private async Task<string> SendQueryAsync(string query, object variables, CancellationToken ct)
    {
        var token = MondayAuth.ResolveToken(_config);
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException(
                "No monday API token. Set mondayApiToken in config.json or the MONDAY_API_TOKEN env var.");

        var payload = JsonSerializer.Serialize(new { query, variables });

        using var req = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        req.Headers.TryAddWithoutValidation("Authorization", token);
        req.Headers.TryAddWithoutValidation("API-Version", "2025-07"); // 2025-07+ exposes Item.description

        using var resp = await Http.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"monday API HTTP {(int)resp.StatusCode}: {Truncate(body)}");
        return body;
    }

    /// <summary>Parse the detail response into description text + updates.</summary>
    public static MondayTaskDetail ParseDetail(string body)
    {
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        if (root.TryGetProperty("errors", out var errors))
            throw new InvalidOperationException($"monday GraphQL error: {errors}");
        if (!root.TryGetProperty("data", out var data)) return MondayTaskDetail.Empty;
        if (!data.TryGetProperty("items", out var items) || items.GetArrayLength() == 0)
            return MondayTaskDetail.Empty;

        var item = items[0];

        // Description: concat the text from each block.
        string? description = null;
        if (item.TryGetProperty("description", out var desc)
            && desc.ValueKind == JsonValueKind.Object
            && desc.TryGetProperty("blocks", out var blocks)
            && blocks.ValueKind == JsonValueKind.Array)
        {
            var parts = new List<string>();
            foreach (var b in blocks.EnumerateArray())
            {
                if (!b.TryGetProperty("content", out var content)) continue;
                var text = content.ValueKind == JsonValueKind.String
                    ? MondayBlockText.FromBlockContentString(content.GetString())
                    : MondayBlockText.FromBlockContent(content);
                if (!string.IsNullOrWhiteSpace(text)) parts.Add(text);
            }
            if (parts.Count > 0) description = string.Join("\n", parts);
        }

        // Updates: newest first (API returns newest first already).
        var updates = new List<MondayUpdate>();
        if (item.TryGetProperty("updates", out var ups) && ups.ValueKind == JsonValueKind.Array)
        {
            foreach (var u in ups.EnumerateArray())
            {
                var text = u.TryGetProperty("text_body", out var tb) ? tb.GetString() : null;
                if (string.IsNullOrWhiteSpace(text)) continue;
                var date = u.TryGetProperty("created_at", out var ca) ? ca.GetString() : null;
                string? author = null;
                if (u.TryGetProperty("creator", out var cr) && cr.ValueKind == JsonValueKind.Object
                    && cr.TryGetProperty("name", out var nm))
                    author = nm.GetString();
                updates.Add(new MondayUpdate(author, date, text!.Trim()));
            }
        }

        return new MondayTaskDetail(description, updates);
    }

    /// <summary>Parse the raw GraphQL response body into normalized tasks.</summary>
    public static List<MondayTask> ParseResponse(string body, string host, long boardId)
    {
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        if (root.TryGetProperty("errors", out var errors))
            throw new InvalidOperationException($"monday GraphQL error: {errors}");

        var tasks = new List<MondayTask>();
        if (!root.TryGetProperty("data", out var data)) return tasks;
        if (!data.TryGetProperty("boards", out var boards) || boards.GetArrayLength() == 0) return tasks;

        foreach (var group in boards[0].GetProperty("groups").EnumerateArray())
        {
            var groupTitle = group.TryGetProperty("title", out var gt) ? gt.GetString() : null;
            var items = group.GetProperty("items_page").GetProperty("items");

            foreach (var item in items.EnumerateArray())
            {
                var id = item.GetProperty("id").GetString() ?? "";
                var name = item.GetProperty("name").GetString() ?? "(untitled)";

                string? status = null, priority = null, branch = null, owner = null;
                foreach (var col in item.GetProperty("column_values").EnumerateArray())
                {
                    var colId = col.GetProperty("id").GetString();
                    var text = col.TryGetProperty("text", out var t) ? t.GetString() : null;
                    switch (colId)
                    {
                        case "status": status = Blank(text); break;
                        case "color_mm2c4cj6": priority = Blank(text); break;
                        case "text_mm1t4h16": branch = Blank(text); break;
                        case "person": owner = Blank(text); break;
                    }
                }

                var task = new MondayTask(
                    Id: id,
                    Name: name,
                    Status: status,
                    StatusColor: MondayStatusColors.StatusHex(status),
                    Priority: priority,
                    Branch: branch,
                    Owner: owner,
                    Group: groupTitle,
                    Url: $"https://{host}/boards/{boardId}/pulses/{id}");

                if (item.TryGetProperty("subitems", out var subs) && subs.ValueKind == JsonValueKind.Array)
                {
                    foreach (var s in subs.EnumerateArray())
                    {
                        var sid = s.GetProperty("id").GetString() ?? "";
                        var sname = s.GetProperty("name").GetString() ?? "(untitled)";
                        string? sstatus = null;
                        if (s.TryGetProperty("column_values", out var scv) && scv.ValueKind == JsonValueKind.Array)
                            foreach (var c in scv.EnumerateArray())
                                if (c.GetProperty("id").GetString() == "color_mm2ej4v4")
                                    sstatus = Blank(c.TryGetProperty("text", out var st) ? st.GetString() : null);
                        task.Subtasks.Add(new MondaySubtask(sid, sname, sstatus,
                            MondayStatusColors.StatusHex(sstatus), $"https://{host}/boards/{boardId}/pulses/{sid}"));
                    }
                }

                tasks.Add(task);
            }
        }

        return tasks;
    }

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

    private static string Truncate(string s) => s.Length > 300 ? s[..300] + "…" : s;
}
