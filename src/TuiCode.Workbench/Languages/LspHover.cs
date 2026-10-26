using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace TuiCode.Workbench.Languages;

/// <summary>A piece of a hover answer: code in <see cref="Language"/> (empty when the server didn't say), or prose when that's null.</summary>
public sealed record HoverPart(string? Language, string Text)
{
    public bool IsCode => Language is not null;
}

public static partial class LspHover
{
    /// <summary>A hover result's contents, of any shape the protocol allows, as code and prose; empty when there's nothing to show.</summary>
    public static IReadOnlyList<HoverPart> Parse(JsonNode? result)
    {
        var parts = new List<HoverPart>();
        Add(parts, result?["contents"]);
        return [.. parts.Where(p => !string.IsNullOrWhiteSpace(p.Text))];
    }

    private static void Add(List<HoverPart> parts, JsonNode? contents)
    {
        switch (contents)
        {
            case JsonValue value when value.TryGetValue<string>(out var markdown):
                Markdown(parts, markdown);
                break;
            case JsonArray array:
                foreach (var item in array) Add(parts, item);
                break;
            case JsonObject markup when markup["kind"]?.GetValue<string>() is { } kind:
                var text = markup["value"]?.GetValue<string>() ?? "";
                if (kind == "markdown") Markdown(parts, text);
                else parts.Add(new HoverPart(null, text.ReplaceLineEndings("\n").Trim('\n')));
                break;
            case JsonObject marked:
                parts.Add(new HoverPart(marked["language"]?.GetValue<string>() ?? "", (marked["value"]?.GetValue<string>() ?? "").ReplaceLineEndings("\n").Trim('\n')));
                break;
        }
    }

    // Fenced code becomes code; the rest is prose, with escapes and rules taken out.
    private static void Markdown(List<HoverPart> parts, string markdown)
    {
        var prose = new StringBuilder();
        List<string>? code = null;
        var language = "";
        foreach (var line in markdown.ReplaceLineEndings("\n").Split('\n'))
        {
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                if (code is null)
                {
                    Flush(parts, prose);
                    code = [];
                    language = trimmed.TrimStart('`').Trim();
                }
                else
                {
                    parts.Add(new HoverPart(language, string.Join('\n', code).Trim('\n')));
                    code = null;
                }
                continue;
            }
            if (code is not null) code.Add(line);
            else prose.Append(Rule().IsMatch(line) ? "" : Escape().Replace(InlineCode().Replace(line, "$2"), "$1")).Append('\n');
        }
        if (code is not null) parts.Add(new HoverPart(language, string.Join('\n', code).Trim('\n')));
        Flush(parts, prose);
    }

    private static void Flush(List<HoverPart> parts, StringBuilder prose)
    {
        if (prose.Length > 0) parts.Add(new HoverPart(null, prose.ToString().Trim('\n')));
        prose.Clear();
    }

    [GeneratedRegex(@"^ {0,3}([-*_])( *\1){2,} *$")]
    private static partial Regex Rule();

    [GeneratedRegex(@"(`+)(.+?)\1")]
    private static partial Regex InlineCode();

    [GeneratedRegex(@"\\([!-/:-@\[-`{-~])")]
    private static partial Regex Escape();
}
