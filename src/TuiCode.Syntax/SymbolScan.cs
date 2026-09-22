using System.Diagnostics;
using TextMateSharp.Grammars;

namespace TuiCode.Syntax;

public enum SymbolKind
{
    Class,
    Interface,
    Enum,
    Struct,
    Trait,
    Method,
    Property,
    Field,
    EnumMember,
    Heading,
}

/// <summary>A definition in the file: its name, what it is, the row it's on, and how many types enclose it.</summary>
public readonly record struct FileSymbol(string Name, SymbolKind Kind, int Line, int Depth);

/// <summary>
/// One buffer's definitions, read from TextMate scopes (#137) and scanned in budgeted slices so a big
/// file fills the picker in rather than blocking the UI. A token is a definition when the grammar names
/// it one (<c>entity.name.type.class</c> and friends, <c>entity.name.variable.property</c>), or when its
/// scope stack says it's being defined here rather than used (<c>meta.function.definition</c>,
/// <c>meta.definition.method</c>, …). Where the grammar gives a flat stack — C# methods get the same
/// <c>entity.name.function.cs</c> for a declaration and a call — the fallback is the line's first name
/// that isn't a type, and only on a line that has already declared something: a modifier or a type
/// ahead of it. That last clause is what keeps <c>await DoWorkAsync(1)</c> out. Markdown has no
/// definitions but headings, which are read whole lines at a time (see <see cref="AddHeading"/>). Each
/// definition also carries the <see cref="FileSymbol.Depth"/> the picker indents it by (see <see cref="DepthAt"/>).
/// </summary>
public sealed class SymbolScan
{
    private static readonly (string Scope, SymbolKind Kind)[] TypeKinds =
    [
        ("entity.name.type.class", SymbolKind.Class), ("entity.name.type.interface", SymbolKind.Interface),
        ("entity.name.type.enum", SymbolKind.Enum), ("entity.name.type.struct", SymbolKind.Struct),
        ("entity.name.type.trait", SymbolKind.Trait),
    ];

    // C# names a property, a field and an enum member outright, so these need no further test.
    private static readonly string[] PropertyNames = ["entity.name.variable.property"];
    private static readonly string[] FieldNames = ["entity.name.variable.field"];
    private static readonly string[] EnumMemberNames = ["entity.name.variable.enum-member", "variable.other.enummember"];

    // TS/JS mark a class field this way, but only its meta.definition.property says it's the declaration.
    private static readonly string[] MemberNames = ["variable.object.property"];
    private static readonly string[] FieldDeclarations = ["meta.field.declaration"];
    private static readonly string[] Interfaces = ["meta.interface"];

    private static readonly string[] MethodNames = ["entity.name.function", "support.function"];

    private static readonly string[] Definitions =
    [
        "meta.function.definition", "meta.definition.function", "meta.definition.method",
        "meta.definition.property", "meta.function", "meta.method",
    ];

    // Inside one of these the name is being used, not defined — and each is a sub-scope of a
    // definition scope in some grammar, so they're checked first.
    private static readonly string[] Uses =
    [
        "meta.block", "meta.body", "meta.function.call", "meta.function-call", "meta.member.access",
        "meta.parameters", "meta.function.parameters", "meta.return.type", "meta.type.annotation",
        "meta.arrow", "meta.macro", "meta.function.decorator",
    ];

    // A type or a modifier ahead of a name is what marks the line a declaration where the stack won't.
    private static readonly string[] Declarators =
    [
        "entity.name.type", "support.type", "keyword.type", "storage.type", "storage.modifier",
    ];

    // Markdown's heading text. The level is in a sibling scope (heading.2, markup.heading.setext.2).
    private static readonly string[] HeadingNames = ["entity.name.section"];

    private static readonly string[] NeverNames = ["comment", "string", "punctuation"];

    // Not symbols themselves, but they count as the line's first name for the flat-stack fallback.
    private static readonly string[] OtherNames = ["entity.name", "variable"];

    // A line that overruns (typically while a cold grammar compiles its regexes) is re-read this many
    // times before its partial tokens stand, as in LineTokenCache.
    internal const int MaxRetries = 2;

    private readonly IGrammar _grammar;
    private readonly IReadOnlyList<string> _lines;
    private readonly List<FileSymbol> _symbols = [];
    private readonly List<int> _enclosing = [];
    private IStateStack? _state;
    private int _next;
    private int _retries;
    private bool _failed;
    private int _lineDepth;
    private int _onThisLine;

    internal SymbolScan(IGrammar grammar, IReadOnlyList<string> lines)
    {
        _grammar = grammar;
        _lines = lines;
    }

    /// <summary>The definitions found so far, in file order.</summary>
    public IReadOnlyList<FileSymbol> Symbols => _symbols;

    /// <summary>Whether the whole buffer has been scanned.</summary>
    public bool Done => _failed || _next >= _lines.Count;

    internal int LinesScanned { get; private set; }

    internal TimeSpan LineTimeLimit { get; set; } = TimeSpan.FromMilliseconds(100);

    /// <summary>Scans on from where the last slice stopped, for at most <paramref name="budget"/>. Returns <see cref="Done"/>.</summary>
    public bool Advance(TimeSpan budget)
    {
        if (Done) return true;
        var clock = Stopwatch.StartNew();
        var scanned = 0;
        while (_next < _lines.Count)
        {
            if (scanned > 0 && clock.Elapsed >= budget) return false;
            scanned++;
            switch (ScanLine(_lines[_next]))
            {
                case LineResult.Scanned: _next++; break;
                case LineResult.Retry: return false;
                default: return true;
            }
        }
        return true;
    }

    private enum LineResult
    {
        Scanned,
        Retry,
        Failed,
    }

    private LineResult ScanLine(string text)
    {
        LinesScanned++;
        // Minified lines cost more to tokenize than the rest of the file put together, as in LineTokenCache.
        if (text.Length > LineTokenCache.MaxLineLength) return LineResult.Scanned;
        _onThisLine = 0;
        IToken[] tokens;
        try
        {
            var clock = Stopwatch.StartNew();
            var result = _grammar.TokenizeLine(text, _state, LineTimeLimit);
            // TextMateSharp's StoppedEarly is internal, so an overrun is timed: its partial tokens would
            // drop definitions, and the line is read again from the same state rather than accepted.
            if (clock.Elapsed >= LineTimeLimit && _retries++ < MaxRetries) return LineResult.Retry;
            _retries = 0;
            _state = result.RuleStack;
            tokens = result.Tokens;
        }
        // A user grammar is untrusted input to TextMateSharp and can throw on the line that uses its broken rule.
        catch (Exception)
        {
            _failed = true;
            return LineResult.Failed;
        }

        if (AddHeading(text, tokens)) return LineResult.Scanned;

        var indent = Indent(text);
        var declared = false;
        var named = false;
        foreach (var token in tokens)
        {
            var name = Slice(text, token);
            if (name.Length == 0) continue;
            var scopes = token.Scopes;
            if (Any(scopes, NeverNames)) continue;

            if (TypeKind(scopes) is { } type) { Add(name, type, indent); named = true; continue; }
            if (Any(scopes, PropertyNames)) { Add(name, SymbolKind.Property, indent); named = true; continue; }
            if (Any(scopes, FieldNames)) { Add(name, SymbolKind.Field, indent); named = true; continue; }
            if (Any(scopes, EnumMemberNames)) { Add(name, SymbolKind.EnumMember, indent); named = true; continue; }
            if (Any(scopes, Declarators)) { declared = true; continue; }

            var kind = Any(scopes, MethodNames) ? SymbolKind.Method
                : Any(scopes, MemberNames) ? MemberKind(scopes)
                : (SymbolKind?)null;
            if (kind is { } member && (DefinedHere(scopes) || IsFlat(scopes) && declared && !named && member == SymbolKind.Method))
                Add(name, member, indent);
            if (kind is not null || Any(scopes, OtherNames)) named = true;
        }
        return LineResult.Scanned;
    }

    /// <summary>
    /// Markdown headings, which are read from the line rather than token by token: inline code or a link
    /// splits one heading's text over several tokens, and a setext heading's text is the line above its
    /// underline. They nest by heading level, which stands in for the indent every other grammar nests by.
    /// </summary>
    private bool AddHeading(string text, IToken[] tokens)
    {
        if (HeadingLevel(tokens, "markup.heading.setext") is { } underlined)
        {
            var title = _next > 0 ? _lines[_next - 1].Trim() : "";
            if (title.Length == 0) return false;
            _symbols.Add(new FileSymbol(title, SymbolKind.Heading, _next - 1, DepthAt(underlined)));
            return true;
        }
        if (HeadingLevel(tokens, "heading") is not { } level) return false;

        // Between the first and the last token of the heading's own text: past the opening #s and any closing run.
        var named = tokens.Where(token => Any(token.Scopes, HeadingNames)).ToArray();
        if (named.Length == 0) return false;
        var start = Math.Clamp(named[0].StartIndex, 0, text.Length);
        var end = Math.Clamp(named[^1].EndIndex, start, text.Length);
        var name = text[start..end].Trim();
        if (name.Length == 0) return false;
        _symbols.Add(new FileSymbol(name, SymbolKind.Heading, _next, DepthAt(level)));
        return true;
    }

    /// <summary>The number in <c>heading.2</c> or <c>markup.heading.setext.2</c>, or null on a line that isn't one.</summary>
    private static int? HeadingLevel(IToken[] tokens, string prefix)
    {
        foreach (var token in tokens)
            foreach (var scope in token.Scopes)
            {
                if (!scope.StartsWith(prefix, StringComparison.Ordinal) || scope.Length <= prefix.Length || scope[prefix.Length] != '.') continue;
                var rest = scope.AsSpan(prefix.Length + 1);
                var dot = rest.IndexOf('.');
                if (int.TryParse(dot < 0 ? rest : rest[..dot], out var level)) return level;
            }
        return null;
    }

    // TS gives a class field and an interface's property the same scopes; only the type around them differs.
    private static SymbolKind MemberKind(IReadOnlyList<string> scopes) =>
        Any(scopes, FieldDeclarations) && !Any(scopes, Interfaces) ? SymbolKind.Field : SymbolKind.Property;

    private void Add(string name, SymbolKind kind, int indent) => _symbols.Add(new FileSymbol(name, kind, _next, DepthAt(indent)));

    /// <summary>
    /// How many definitions enclose this one, from the line's indent rather than the scope stack — C# nests
    /// nothing where TypeScript nests everything, so layout is the one signal every grammar shares. A
    /// definition on the same line as the one before it (<c>interface Thing { a: number }</c>) sits inside it.
    /// </summary>
    private int DepthAt(int indent)
    {
        if (_onThisLine++ > 0) return _lineDepth + 1;
        while (_enclosing.Count > 0 && _enclosing[^1] >= indent) _enclosing.RemoveAt(_enclosing.Count - 1);
        _lineDepth = _enclosing.Count;
        _enclosing.Add(indent);
        return _lineDepth;
    }

    private static int Indent(string text)
    {
        var i = 0;
        while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
        return i;
    }

    private static SymbolKind? TypeKind(IReadOnlyList<string> scopes)
    {
        foreach (var (scope, kind) in TypeKinds)
            if (scopes.Any(s => Matches(s, scope)))
                return kind;
        return null;
    }

    private static bool DefinedHere(IReadOnlyList<string> scopes) => Any(scopes, Definitions) && !Any(scopes, Uses);

    private static bool IsFlat(IReadOnlyList<string> scopes) => !scopes.Any(s => Matches(s, "meta"));

    private static bool Any(IReadOnlyList<string> scopes, string[] patterns) =>
        scopes.Any(s => patterns.Any(p => Matches(s, p)));

    /// <summary>Whole-segment prefix, so <c>meta.function</c> matches <c>meta.function.definition</c> but not <c>meta.function-call</c>.</summary>
    private static bool Matches(string scope, string pattern) =>
        scope.StartsWith(pattern, StringComparison.Ordinal)
        && (scope.Length == pattern.Length || scope[pattern.Length] == '.');

    private static string Slice(string text, IToken token)
    {
        var start = Math.Clamp(token.StartIndex, 0, text.Length);
        var end = Math.Clamp(token.EndIndex, start, text.Length);
        return text[start..end].Trim();
    }
}
