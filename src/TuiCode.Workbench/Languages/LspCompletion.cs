using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace TuiCode.Workbench.Languages;

/// <summary>Where on <see cref="Line"/> an item's text goes, in UTF-16 chars, as the server said.</summary>
public readonly record struct CompletionEdit(int Line, int Start, int End, string NewText);

/// <summary>One thing that fits at the caret. <see cref="Kind"/> is the protocol's <c>CompletionItemKind</c>, 0 when the server didn't say.</summary>
public sealed record CompletionItem(string Label, int Kind = 0, string? Detail = null, string? FilterText = null, string? SortText = null,
    string? InsertText = null, CompletionEdit? Edit = null)
{
    public string Filter => FilterText ?? Label;

    public string Sort => SortText ?? Label;

    /// <summary>The text accepting it puts in.</summary>
    public string Text => Edit?.NewText ?? InsertText ?? Label;
}

/// <summary>A completion answer. <see cref="IsIncomplete"/> says typing more should ask again rather than narrow this list.</summary>
public sealed record CompletionList(IReadOnlyList<CompletionItem> Items, bool IsIncomplete)
{
    public static readonly CompletionList Empty = new([], false);

    /// <summary>Each item as the server sent it, for <c>completionItem/resolve</c>.</summary>
    public IReadOnlyDictionary<CompletionItem, JsonObject> Sent { get; init; } = new Dictionary<CompletionItem, JsonObject>(ReferenceEqualityComparer.Instance);
}

public static partial class LspCompletion
{
    private const int SnippetFormat = 2;

    /// <summary>A <c>CompletionItem[]</c> or <c>CompletionList</c>, plain text only.</summary>
    public static CompletionList Parse(JsonNode? result)
    {
        var (items, incomplete) = result switch
        {
            JsonArray array => (array, false),
            JsonObject list => (list["items"] as JsonArray ?? [], list["isIncomplete"]?.GetValueKind() == JsonValueKind.True),
            _ => ([], false),
        };
        var defaults = (result as JsonObject)?["itemDefaults"];
        var sent = new Dictionary<CompletionItem, JsonObject>(ReferenceEqualityComparer.Instance);
        foreach (var item in items.OfType<JsonObject>())
            if (Item(item, defaults) is { } parsed) sent[parsed] = item;
        return new CompletionList([.. sent.Keys], incomplete) { Sent = sent };
    }

    /// <summary>The characters the server says start completion, from its initialize result's capabilities.</summary>
    public static IReadOnlySet<string> TriggerCharacters(JsonNode? capabilities) =>
        (capabilities?["completionProvider"]?["triggerCharacters"] as JsonArray ?? [])
            .Select(c => c?.GetValueKind() == JsonValueKind.String ? c.GetValue<string>() : null)
            .OfType<string>().Where(c => c.Length > 0).ToHashSet(StringComparer.Ordinal);

    /// <summary>The detail a <c>completionItem/resolve</c> answer gives, if any.</summary>
    public static string? ResolvedDetail(JsonNode? item) => String(item?["detail"]) ?? String(item?["labelDetails"]?["detail"]);

    private static CompletionItem? Item(JsonObject item, JsonNode? defaults)
    {
        if (String(item["label"]) is not { Length: > 0 } label) return null;
        var snippet = (Int(item["insertTextFormat"]) ?? Int(defaults?["insertTextFormat"])) == SnippetFormat;
        string? Plain(string? text) => text is null ? null : snippet ? StripSnippet(text) : text;
        var edit = Edit(item["textEdit"], String(item["textEditText"]) ?? String(item["insertText"]) ?? label)
            ?? Edit(defaults?["editRange"], String(item["textEditText"]) ?? String(item["insertText"]) ?? label);
        return new CompletionItem(label, Int(item["kind"]) ?? 0, String(item["detail"]) ?? String(item["labelDetails"]?["detail"]),
            String(item["filterText"]), String(item["sortText"]), Plain(String(item["insertText"])),
            edit is { } e ? e with { NewText = Plain(e.NewText)! } : null);
    }

    // A TextEdit, an InsertReplaceEdit (its insert range), or a bare range or itemDefaults' editRange, on one line only.
    private static CompletionEdit? Edit(JsonNode? edit, string fallbackText)
    {
        if (edit is not JsonObject value) return null;
        var range = value["range"] ?? value["insert"] ?? (value["start"] is not null ? value : null);
        if (range?["start"] is not { } start || range["end"] is not { } end) return null;
        var line = Int(start["line"]);
        if (line is null || line != Int(end["line"]) || Int(start["character"]) is not { } from || Int(end["character"]) is not { } to) return null;
        return new CompletionEdit(line.Value, from, to, String(value["newText"]) ?? fallbackText);
    }

    private static string StripSnippet(string text) =>
        Escaped().Replace(Placeholder().Replace(Tabstop().Replace(text, ""), "$1"), "$1");

    private static string? String(JsonNode? node) => node?.GetValueKind() == JsonValueKind.String ? node.GetValue<string>() : null;

    private static int? Int(JsonNode? node) => node?.GetValueKind() == JsonValueKind.Number ? node.GetValue<int>() : null;

    [GeneratedRegex(@"\$\{\d+:([^}]*)\}")]
    private static partial Regex Placeholder();

    [GeneratedRegex(@"\$\{\d+\}|\$\d+")]
    private static partial Regex Tabstop();

    [GeneratedRegex(@"\\([$}\\])")]
    private static partial Regex Escaped();
}
