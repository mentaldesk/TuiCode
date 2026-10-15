using Terminal.Gui.Text;
using TuiCode.Editor;
using TuiCode.Syntax;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace TuiCode.Workbench.Assistant;

/// <summary>The AI assistance tab's transcript: <see cref="Conversation"/> laid out to the width, scrolled, code fences coloured.</summary>
public sealed class ChatView : View
{
    private static readonly TimeSpan SyntaxBudget = TimeSpan.FromMilliseconds(15);

    private readonly Conversation _conversation;
    private readonly SyntaxHighlighter? _syntax;
    private readonly TokenPalette _palette = new();
    private readonly Dictionary<int, LineTokenCache?> _caches = [];
    private IReadOnlyList<ChatLine> _lines = [];
    private IReadOnlyList<CodeBlock> _blocks = [];
    private (int Version, int Width) _laidOut = (-1, -1);

    public ChatView(Conversation conversation, SyntaxHighlighter? syntax = null)
    {
        _conversation = conversation;
        _syntax = syntax;
        CanFocus = true;
        ViewportSettings |= ViewportSettingsFlags.HasVerticalScrollBar;
        VerticalScrollBar.VisibilityMode = ScrollBarVisibilityMode.Auto;

        AddCommand(Command.Up, () => ScrollBy(-1));
        AddCommand(Command.Down, () => ScrollBy(1));
        AddCommand(Command.PageUp, () => ScrollBy(-Math.Max(Viewport.Height - 1, 1)));
        AddCommand(Command.PageDown, () => ScrollBy(Math.Max(Viewport.Height - 1, 1)));
        AddCommand(Command.Start, () => ScrollTo(0));
        AddCommand(Command.End, () => ScrollTo(int.MaxValue));
        KeyBindings.Add(Key.CursorUp, Command.Up);
        KeyBindings.Add(Key.CursorDown, Command.Down);
        KeyBindings.Add(Key.PageUp, Command.PageUp);
        KeyBindings.Add(Key.PageDown, Command.PageDown);
        KeyBindings.Add(Key.Home, Command.Start);
        KeyBindings.Add(Key.End, Command.End);
    }

    /// <summary>The rows as laid out at the last draw, for tests.</summary>
    internal IReadOnlyList<ChatLine> Lines
    {
        get
        {
            LayOut();
            return _lines;
        }
    }

    /// <summary>Whether the last row is in view, so new text should keep it there.</summary>
    public bool AtBottom => Viewport.Y + Viewport.Height >= _lines.Count;

    /// <summary>Lays out again after the conversation changed, following the end if it was in view.</summary>
    public void Refresh()
    {
        var follow = AtBottom;
        LayOut();
        if (follow) ScrollTo(int.MaxValue);
        SetNeedsDraw();
    }

    private int TextWidth => Math.Max(Viewport.Width - 1, 1);

    private void LayOut()
    {
        if (_laidOut == (_conversation.Version, TextWidth)) return;
        _laidOut = (_conversation.Version, TextWidth);
        (_lines, _blocks) = _conversation.Render(TextWidth);
        foreach (var index in _caches.Keys.Where(i => i >= _blocks.Count).ToList()) _caches.Remove(index);
        SetContentSize(new System.Drawing.Size(Math.Max(Viewport.Width, 1), Math.Max(_lines.Count, 1)));
    }

    private bool? ScrollBy(int rows) => ScrollTo(Viewport.Y + rows);

    private bool? ScrollTo(int top)
    {
        top = Math.Clamp(top, 0, Math.Max(_lines.Count - Viewport.Height, 0));
        if (top != Viewport.Y) Viewport = Viewport with { Y = top };
        return true;
    }

    protected override bool OnMouseEvent(Mouse mouse)
    {
        if (mouse.Flags.HasFlag(MouseFlags.WheeledUp)) return ScrollBy(-3) == true;
        if (mouse.Flags.HasFlag(MouseFlags.WheeledDown)) return ScrollBy(3) == true;
        return base.OnMouseEvent(mouse);
    }

    protected override void OnViewportChanged(DrawEventArgs e)
    {
        base.OnViewportChanged(e);
        if (_laidOut.Width != TextWidth) Refresh();
    }

    protected override bool OnDrawingContent(DrawContext? context)
    {
        LayOut();
        var normal = GetAttributeForRole(VisualRole.Normal);
        if (_syntax is not null) _palette.Sync(_syntax);
        var lexing = false;
        for (var row = 0; row < Viewport.Height; row++)
        {
            var index = Viewport.Y + row;
            Move(0, row);
            SetAttribute(normal);
            AddStr(new string(' ', Viewport.Width));
            if (index >= _lines.Count) continue;
            var line = _lines[index];
            if (line.Kind == ChatLineKind.Code)
            {
                lexing |= DrawCode(line, row, normal);
                continue;
            }
            SetAttribute(line.Kind switch
            {
                ChatLineKind.Heading => normal with { Style = normal.Style | TextStyle.Bold },
                ChatLineKind.Tool or ChatLineKind.Note => normal with { Style = normal.Style | TextStyle.Faint },
                _ => normal,
            });
            Move(0, row);
            AddStr(line.Text);
        }
        if (lexing) App?.Invoke(SetNeedsDraw);
        return true;
    }

    // Lexes the block through this line within the budget; true while it still has more to lex.
    private bool DrawCode(ChatLine line, int row, Attribute normal)
    {
        var tokens = TokensFor(line.Block, line.Line, out var pending);
        var chars = line.Offset;
        var token = 0;
        var col = 0;
        foreach (var grapheme in GraphemeHelper.GetGraphemes(line.Text))
        {
            var attribute = normal;
            if (tokens is { Length: > 0 })
            {
                while (token + 2 < tokens.Length && tokens[token + 2] <= chars) token += 2;
                attribute = _palette.Apply(normal, tokens[token + 1]);
            }
            chars += grapheme.Length;
            SetAttribute(attribute);
            AddStr(col, row, grapheme);
            col += Math.Max(grapheme.GetColumns(), 1);
        }
        return pending;
    }

    private int[]? TokensFor(int block, int line, out bool pending)
    {
        pending = false;
        if (_syntax is null || block < 0 || block >= _blocks.Count) return null;
        if (!_caches.TryGetValue(block, out var cache))
            _caches[block] = cache = _syntax.CreateCache(Language(_blocks[block].Language));
        if (cache is null) return null;
        cache.Update(_blocks[block].Lines);
        pending = !cache.TokenizeThrough(line, SyntaxBudget);
        return cache.TokensFor(line);
    }

    private SyntaxLanguage? Language(string fence)
    {
        if (_syntax is null || fence.Length == 0) return null;
        var name = fence.Split(' ', 2)[0];
        return _syntax.LanguageById(name) ?? _syntax.LanguageForFile("file." + name);
    }
}
