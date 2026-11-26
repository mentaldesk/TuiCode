using TuiCode.Editor;
using TuiCode.Syntax;

namespace TuiCode.Workbench.Usages;

public sealed record Declaration(string Name, int Line, int Character, int EndLine);

/// <summary>The definitions in one file, which say what a line sits in and which lines define a name (#713).</summary>
public sealed class FileDefinitions
{
    public static FileDefinitions None { get; } = new([]);

    private readonly IReadOnlyList<Declaration> _declarations;

    public FileDefinitions(IReadOnlyList<Declaration> declarations) => _declarations = declarations;

    public IReadOnlyList<Declaration> Declarations => _declarations;

    public Declaration? Around(int line)
    {
        Declaration? innermost = null;
        foreach (var declaration in _declarations)
            if (declaration.Line <= line && line <= declaration.EndLine
                && (innermost is null || declaration.Line > innermost.Line || declaration.Line == innermost.Line && declaration.EndLine <= innermost.EndLine))
                innermost = declaration;
        return innermost;
    }

    public bool Defines(string name, int line) => _declarations.Any(d => d.Line == line && d.Name == name);

    public static bool IsMsBuild(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".targets", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".props", StringComparison.OrdinalIgnoreCase)
            || extension.EndsWith("proj", StringComparison.OrdinalIgnoreCase);
    }

    public static FileDefinitions FromSymbols(IReadOnlyList<FileSymbol> symbols, IReadOnlyList<string> lines)
    {
        var declarations = new List<Declaration>(symbols.Count);
        for (var i = 0; i < symbols.Count; i++)
        {
            var symbol = symbols[i];
            if (symbol.Line >= lines.Count) continue;
            var character = Math.Max(0, WordAt(lines[symbol.Line], symbol.Name));
            declarations.Add(new Declaration(symbol.Name, symbol.Line, character, StickyLines.EndOf(symbols, i, lines)));
        }
        return new FileDefinitions(declarations);
    }

    public static FileDefinitions MsBuild(IReadOnlyList<string> lines)
    {
        var declarations = new List<Declaration>();
        var open = new List<(string Element, Declaration? Defines)>();
        foreach (var tag in MsBuildTags.Read(lines))
        {
            if (tag.Closing)
            {
                var at = open.FindLastIndex(o => o.Element == tag.Element);
                if (at < 0) continue;
                for (var i = open.Count - 1; i >= at; i--)
                    if (open[i].Defines is { } defined) declarations.Add(defined with { EndLine = tag.EndLine });
                open.RemoveRange(at, open.Count - at);
                continue;
            }
            var parent = open.Count > 0 ? open[^1].Element : null;
            Declaration? defines = tag.Element == "Target" && tag.Attribute("Name") is { } name
                ? new Declaration(name.Text, name.Line, name.Character, tag.EndLine)
                : parent is "PropertyGroup" or "ItemGroup"
                    ? new Declaration(tag.Element, tag.Line, tag.Character, tag.EndLine)
                    : null;
            if (tag.SelfClosing)
            {
                if (defines is not null) declarations.Add(defines);
                continue;
            }
            open.Add((tag.Element, defines));
        }
        foreach (var (_, defines) in open)
            if (defines is not null) declarations.Add(defines with { EndLine = lines.Count - 1 });
        return new FileDefinitions([.. declarations.OrderBy(d => d.Line).ThenBy(d => d.Character)]);
    }

    private static int WordAt(string line, string word)
    {
        for (var at = line.IndexOf(word, StringComparison.Ordinal); at >= 0; at = line.IndexOf(word, at + 1, StringComparison.Ordinal))
            if ((at == 0 || !IsWordPart(line[at - 1])) && (at + word.Length >= line.Length || !IsWordPart(line[at + word.Length])))
                return at;
        return -1;
    }

    private static bool IsWordPart(char c) => char.IsLetterOrDigit(c) || c == '_';
}

/// <summary>An XML file's tags, read leniently: comments, CDATA and processing instructions skipped.</summary>
internal static class MsBuildTags
{
    internal sealed record Value(string Text, int Line, int Character);

    internal sealed record Tag(string Element, int Line, int Character, int EndLine, bool Closing, bool SelfClosing, IReadOnlyDictionary<string, Value> Attributes)
    {
        public Value? Attribute(string name) => Attributes.GetValueOrDefault(name);
    }

    public static IEnumerable<Tag> Read(IReadOnlyList<string> lines)
    {
        var (line, character) = (0, 0);
        while (Find(lines, '<', ref line, ref character))
        {
            if (StartsWith(lines, line, character, "<!--")) { Skip(lines, "-->", ref line, ref character); continue; }
            if (StartsWith(lines, line, character, "<![CDATA[")) { Skip(lines, "]]>", ref line, ref character); continue; }
            if (StartsWith(lines, line, character, "<?") || StartsWith(lines, line, character, "<!")) { Skip(lines, ">", ref line, ref character); continue; }
            var (startLine, startCharacter) = (line, character);
            character++;
            var closing = character < lines[line].Length && lines[line][character] == '/';
            if (closing) character++;
            var nameAt = character;
            while (character < lines[line].Length && IsNamePart(lines[line][character])) character++;
            var element = lines[line][nameAt..character];
            if (element.Length == 0) continue;
            var attributes = new Dictionary<string, Value>(StringComparer.Ordinal);
            var selfClosing = false;
            while (true)
            {
                if (!SkipSpace(lines, ref line, ref character)) yield break;
                var c = lines[line][character];
                if (c == '>') { character++; break; }
                if (c == '/' && character + 1 < lines[line].Length && lines[line][character + 1] == '>')
                {
                    selfClosing = true;
                    character += 2;
                    break;
                }
                var attributeAt = character;
                while (character < lines[line].Length && IsNamePart(lines[line][character])) character++;
                if (character == attributeAt) { character++; continue; }
                var attribute = lines[line][attributeAt..character];
                if (!SkipSpace(lines, ref line, ref character)) yield break;
                if (lines[line][character] != '=') continue;
                character++;
                if (!SkipSpace(lines, ref line, ref character)) yield break;
                var quote = lines[line][character];
                if (quote is not ('"' or '\'')) continue;
                var (valueLine, valueCharacter) = (line, character + 1);
                character++;
                if (!Find(lines, quote, ref line, ref character)) yield break;
                var value = valueLine == line ? lines[line][valueCharacter..character] : lines[valueLine][valueCharacter..];
                attributes.TryAdd(attribute, new Value(value, valueLine, valueCharacter));
                character++;
            }
            yield return new Tag(element, startLine, startCharacter + (closing ? 2 : 1), line, closing, selfClosing, attributes);
        }
    }

    private static bool IsNamePart(char c) => char.IsLetterOrDigit(c) || c is '_' or '-' or '.' or ':';

    private static bool StartsWith(IReadOnlyList<string> lines, int line, int character, string text) =>
        string.CompareOrdinal(lines[line], character, text, 0, text.Length) == 0;

    private static bool Find(IReadOnlyList<string> lines, char c, ref int line, ref int character)
    {
        for (; line < lines.Count; line++, character = 0)
        {
            var at = character < lines[line].Length ? lines[line].IndexOf(c, character) : -1;
            if (at < 0) continue;
            character = at;
            return true;
        }
        return false;
    }

    private static void Skip(IReadOnlyList<string> lines, string end, ref int line, ref int character)
    {
        for (; line < lines.Count; line++, character = 0)
        {
            var at = character < lines[line].Length ? lines[line].IndexOf(end, character, StringComparison.Ordinal) : -1;
            if (at < 0) continue;
            character = at + end.Length;
            return;
        }
    }

    private static bool SkipSpace(IReadOnlyList<string> lines, ref int line, ref int character)
    {
        for (; line < lines.Count; line++, character = 0)
        {
            while (character < lines[line].Length && char.IsWhiteSpace(lines[line][character])) character++;
            if (character < lines[line].Length) return true;
        }
        return false;
    }
}
