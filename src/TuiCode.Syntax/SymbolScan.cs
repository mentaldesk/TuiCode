using System.Diagnostics;
using TextMateSharp.Grammars;

namespace TuiCode.Syntax;

public enum SymbolKind
{
    Type,
    Method,
    Property,
}

/// <summary>A definition in the file: its name, what it is, and the row it's on.</summary>
public readonly record struct FileSymbol(string Name, SymbolKind Kind, int Line);

/// <summary>
/// One buffer's definitions, read from TextMate scopes (#137) and scanned in budgeted slices so a big
/// file fills the picker in rather than blocking the UI. A token is a definition when the grammar names
/// it one (<c>entity.name.type.class</c> and friends, <c>entity.name.variable.property</c>), or when its
/// scope stack says it's being defined here rather than used (<c>meta.function.definition</c>,
/// <c>meta.definition.method</c>, …). Where the grammar gives a flat stack — C# methods get the same
/// <c>entity.name.function.cs</c> for a declaration and a call — the fallback is the line's first name
/// that isn't a type, and only on a line that has already declared something: a modifier or a type
/// ahead of it. That last clause is what keeps <c>await DoWorkAsync(1)</c> out.
/// </summary>
public sealed class SymbolScan
{
    private static readonly string[] TypeKinds =
    [
        "entity.name.type.class", "entity.name.type.interface", "entity.name.type.enum",
        "entity.name.type.struct", "entity.name.type.trait",
    ];

    // C# gives this to properties and to nothing else, so it needs no further test.
    private static readonly string[] PropertyNames = ["entity.name.variable.property"];

    // TS/JS mark a class field this way, but only its meta.definition.property says it's the declaration.
    private static readonly string[] MemberNames = ["variable.object.property"];

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

    private static readonly string[] NeverNames = ["comment", "string", "punctuation"];

    // Not symbols themselves, but they count as the line's first name for the flat-stack fallback.
    private static readonly string[] OtherNames = ["entity.name", "variable"];

    // A line that overruns (typically while a cold grammar compiles its regexes) is re-read this many
    // times before its partial tokens stand, as in LineTokenCache.
    internal const int MaxRetries = 2;

    private readonly IGrammar _grammar;
    private readonly IReadOnlyList<string> _lines;
    private readonly List<FileSymbol> _symbols = [];
    private IStateStack? _state;
    private int _next;
    private int _retries;
    private bool _failed;

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

        var declared = false;
        var named = false;
        foreach (var token in tokens)
        {
            var name = Slice(text, token);
            if (name.Length == 0) continue;
            var scopes = token.Scopes;
            if (Any(scopes, NeverNames)) continue;

            if (Any(scopes, TypeKinds)) { Add(name, SymbolKind.Type); named = true; continue; }
            if (Any(scopes, PropertyNames)) { Add(name, SymbolKind.Property); named = true; continue; }
            if (Any(scopes, Declarators)) { declared = true; continue; }

            var kind = Any(scopes, MethodNames) ? SymbolKind.Method
                : Any(scopes, MemberNames) ? SymbolKind.Property
                : (SymbolKind?)null;
            if (kind is { } member && (DefinedHere(scopes) || IsFlat(scopes) && declared && !named && member == SymbolKind.Method))
                Add(name, member);
            if (kind is not null || Any(scopes, OtherNames)) named = true;
        }
        return LineResult.Scanned;
    }

    private void Add(string name, SymbolKind kind) => _symbols.Add(new FileSymbol(name, kind, _next));

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
