using TuiCode.Syntax;
using TuiCode.Workbench.Controls;
using TuiCode.Workbench.Settings;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace TuiCode.Workbench.Assistant;

/// <summary>The AI assistance tab's transcript: <see cref="Conversation"/> laid out to the width, scrolled, code fences coloured.</summary>
public sealed class ChatView : View
{
    private readonly Conversation _conversation;
    private readonly CodeBlockColours _code;
    private IReadOnlyList<ChatLine> _lines = [];
    private IReadOnlyList<CodeBlock> _blocks = [];
    private (int Version, int Width) _laidOut = (-1, -1);

    public ChatView(Conversation conversation, SyntaxHighlighter? syntax = null)
    {
        _conversation = conversation;
        _code = new CodeBlockColours(syntax);
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

    /// <summary>Folds or unfolds the tool calls (#618), keeping the row at the top in view, or the end if it was.</summary>
    public void ShowToolCallsInFull(bool inFull)
    {
        LayOut();
        var follow = AtBottom;
        var top = Viewport.Y < _lines.Count ? _lines[Viewport.Y] : null;
        var within = top is null ? 0 : Viewport.Y - FirstRowOf(top.Source);
        _conversation.ToolCallsInFull = inFull;
        LayOut();
        if (follow || top is null) ScrollTo(int.MaxValue);
        else
        {
            var first = FirstRowOf(top.Source);
            ScrollTo(first + (first < _lines.Count && _lines[first].Source == top.Source ? within : 0));
        }
        SetNeedsDraw();
    }

    // The first row from the same source or a later one, since a folded call has no row.
    private int FirstRowOf(int source)
    {
        for (var i = 0; i < _lines.Count; i++)
            if (_lines[i].Source >= source) return i;
        return _lines.Count;
    }

    private int TextWidth =>Math.Max(Viewport.Width - 1, 1);

    private void LayOut()
    {
        if (_laidOut == (_conversation.Version, TextWidth)) return;
        _laidOut = (_conversation.Version, TextWidth);
        (_lines, _blocks) = _conversation.Render(TextWidth);
        _code.Keep(_blocks.Count);
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
        var mine = RowStripes.Stripe(normal);
        _code.BeginDraw();
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
            var attribute = normal;
            var left = 0;
            if (line.Role == ChatRole.User)
            {
                (attribute, left) = (mine, Conversation.Gutter);
                SetAttribute(mine);
                Move(left, row);
                AddStr(new string(' ', Math.Max(TextWidth - left, 0)));
            }
            SetAttribute(line.Kind switch
            {
                ChatLineKind.Heading => attribute with { Style = attribute.Style | TextStyle.Bold },
                ChatLineKind.Tool or ChatLineKind.Note => attribute with { Style = attribute.Style | TextStyle.Faint },
                _ => attribute,
            });
            Move(left, row);
            AddStr(line.Text);
        }
        if (lexing) App?.Invoke(SetNeedsDraw);
        return true;
    }

    private bool DrawCode(ChatLine line, int row, Attribute normal)
    {
        var block = line.Block >= 0 && line.Block < _blocks.Count ? _blocks[line.Block] : null;
        return _code.Draw(this, block is null ? -1 : line.Block, () => _code.ForFence(block!.Language), block?.Lines ?? [], line.Line, line.Text, line.Offset, 0, row, normal);
    }
}
