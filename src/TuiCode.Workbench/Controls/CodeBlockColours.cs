using Terminal.Gui.Text;
using TuiCode.Editor;
using TuiCode.Syntax;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace TuiCode.Workbench.Controls;

/// <summary>Colours the rows of code blocks shown outside an editor, lexing each block lazily within a frame's budget.</summary>
internal sealed class CodeBlockColours(SyntaxHighlighter? syntax)
{
    private static readonly TimeSpan SyntaxBudget = TimeSpan.FromMilliseconds(15);

    private readonly TokenPalette _palette = new();
    private readonly Dictionary<int, LineTokenCache?> _caches = [];

    public SyntaxHighlighter? Syntax => syntax;

    public void BeginDraw()
    {
        if (syntax is not null) _palette.Sync(syntax);
    }

    /// <summary>Drops the caches of blocks from <paramref name="count"/> on, after the blocks were laid out again.</summary>
    public void Keep(int count)
    {
        foreach (var index in _caches.Keys.Where(i => i >= count).ToList()) _caches.Remove(index);
    }

    /// <summary>The grammar a fence names, by id or as an extension.</summary>
    public SyntaxLanguage? ForFence(string fence)
    {
        if (syntax is null || fence.Length == 0) return null;
        var name = fence.Split(' ', 2)[0];
        return syntax.LanguageById(name) ?? syntax.LanguageForFile("file." + name);
    }

    /// <summary>Draws <paramref name="text"/>, <paramref name="offset"/> chars into line <paramref name="line"/> of block <paramref name="block"/>; true while the block has more to lex.</summary>
    public bool Draw(View view, int block, Func<SyntaxLanguage?> language, IReadOnlyList<string> lines, int line, string text, int offset, int col, int row, Attribute normal)
    {
        var tokens = TokensFor(block, language, lines, line, out var pending);
        var chars = offset;
        var token = 0;
        foreach (var grapheme in GraphemeHelper.GetGraphemes(text))
        {
            var attribute = normal;
            if (tokens is { Length: > 0 })
            {
                while (token + 2 < tokens.Length && tokens[token + 2] <= chars) token += 2;
                attribute = _palette.Apply(normal, tokens[token + 1]);
            }
            chars += grapheme.Length;
            view.SetAttribute(attribute);
            view.AddStr(col, row, grapheme);
            col += Math.Max(grapheme.GetColumns(), 1);
        }
        return pending;
    }

    private int[]? TokensFor(int block, Func<SyntaxLanguage?> language, IReadOnlyList<string> lines, int line, out bool pending)
    {
        pending = false;
        if (syntax is null || block < 0) return null;
        if (!_caches.TryGetValue(block, out var cache))
            _caches[block] = cache = syntax.CreateCache(language());
        if (cache is null) return null;
        cache.Update(lines);
        pending = !cache.TokenizeThrough(line, SyntaxBudget);
        return cache.TokensFor(line);
    }
}
