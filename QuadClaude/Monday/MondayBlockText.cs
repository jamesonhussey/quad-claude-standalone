using System.Text;
using System.Text.Json;

namespace QuadClaude.Monday;

/// <summary>
/// Extracts plain text from monday.com document blocks. Block content is a
/// Quill-style delta: { "deltaFormat": [ { "insert": "text" }, ... ] }.
/// We pull the insert strings; anything we can't parse is skipped.
/// </summary>
public static class MondayBlockText
{
    /// <summary>Extract text from one block's content JSON (already deserialized element).</summary>
    public static string FromBlockContent(JsonElement content)
    {
        var sb = new StringBuilder();
        if (content.ValueKind == JsonValueKind.Object
            && content.TryGetProperty("deltaFormat", out var delta)
            && delta.ValueKind == JsonValueKind.Array)
        {
            foreach (var op in delta.EnumerateArray())
            {
                if (op.ValueKind == JsonValueKind.Object
                    && op.TryGetProperty("insert", out var ins)
                    && ins.ValueKind == JsonValueKind.String)
                    sb.Append(ins.GetString());
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// Extract text from a block whose "content" came back as a JSON *string*
    /// (the API returns the JSON scalar as a string). Returns "" on failure.
    /// </summary>
    public static string FromBlockContentString(string? contentJson)
    {
        if (string.IsNullOrWhiteSpace(contentJson)) return "";
        try
        {
            using var doc = JsonDocument.Parse(contentJson);
            return FromBlockContent(doc.RootElement);
        }
        catch { return ""; }
    }
}
