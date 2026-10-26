using Terminal.Gui.Text;
using TuiCode.Abstractions;
using TuiCode.Syntax;
using TuiCode.Workbench.Controls;
using TuiCode.Workbench.Services;
using ButtonKind = MentalDesk.Tui.Dialogs.ButtonKind;

namespace TuiCode.Workbench.Navigation;

/// <summary>Quick info's box under the caret's line. It never takes focus: the host feeds it its keys and closes it.</summary>
public sealed class QuickInfoView : View
{
    private readonly QuickInfo _info;
    private readonly Func<string, SyntaxLanguage?> _language;
    private readonly CodeBlockColours _code;
    private readonly ICommandService _scopeCommands;
    private readonly IKeybindingService _scopeKeybindings;
    private readonly Button _goTo;
    private readonly Button _close;

    public IKeybindingService Scope => _scopeKeybindings;

    public event EventHandler? Cancelled;

    public event EventHandler? Submitted;

    /// <param name="fileGrammar">Colours code whose fence names no grammar we have.</param>
    public QuickInfoView(QuickInfo info, SyntaxHighlighter? syntax, SyntaxLanguage? fileGrammar)
    {
        _info = info;
        _code = new CodeBlockColours(syntax);
        _language = fence => _code.ForFence(fence) ?? fileGrammar;
        Title = "Quick info";
        BorderStyle = LineStyle.Single;
        CanFocus = false;
        ViewportSettings |= ViewportSettingsFlags.HasVerticalScrollBar;
        VerticalScrollBar.VisibilityMode = ScrollBarVisibilityMode.Auto;

        _scopeCommands = new CommandService();
        _scopeKeybindings = new KeybindingService(_scopeCommands);
        _scopeCommands.Register(CommandIds.QuickInfoClose, () => Cancelled?.Invoke(this, EventArgs.Empty));
        _scopeCommands.Register(CommandIds.QuickInfoGoToDefinition, () => Submitted?.Invoke(this, EventArgs.Empty));
        _scopeCommands.Register(CommandIds.QuickInfoUp, () => ScrollBy(-1));
        _scopeCommands.Register(CommandIds.QuickInfoDown, () => ScrollBy(1));
        _scopeCommands.Register(CommandIds.QuickInfoPageUp, () => ScrollBy(-PageHeight));
        _scopeCommands.Register(CommandIds.QuickInfoPageDown, () => ScrollBy(PageHeight));
        _scopeKeybindings.Bind("Esc", CommandIds.QuickInfoClose);
        _scopeKeybindings.Bind("Enter", CommandIds.QuickInfoGoToDefinition);
        _scopeKeybindings.Bind("CursorUp", CommandIds.QuickInfoUp);
        _scopeKeybindings.Bind("CursorDown", CommandIds.QuickInfoDown);
        _scopeKeybindings.Bind("PageUp", CommandIds.QuickInfoPageUp);
        _scopeKeybindings.Bind("PageDown", CommandIds.QuickInfoPageDown);

        _goTo = Keyed(ButtonKind.Primary, CommandIds.QuickInfoGoToDefinition);
        _close = Keyed(ButtonKind.Secondary, CommandIds.QuickInfoClose);
        _close.X = Pos.AnchorEnd() - 2;
        _close.Y = _goTo.Y = Pos.AnchorEnd(1);
        FitButtons(int.MaxValue);
        Border.GetOrCreateView().Add(_goTo, _close);
        SetContentSize(new System.Drawing.Size(1, Math.Max(info.Rows.Count, 1)));
    }

    /// <summary>The width both buttons and the border between and around them need.</summary>
    public int ButtonsWidth => _goTo.Text.GetColumns() + _close.Text.GetColumns() + 6;

    /// <summary>The box's size for its content, border and padding, less the scroll bar it needs if it ends up shorter.</summary>
    public (int Width, int Height) Wanted => (Math.Max(_info.Width + QuickInfo.Frame, ButtonsWidth), _info.Rows.Count + 2);

    internal int Top => Viewport.Y;

    internal IReadOnlyList<string> Rows => [.. _info.Rows.Select(r => r.Text)];

    internal Button GoToButton => _goTo;

    internal Button CloseButton => _close;

    private static readonly (string GoTo, string Close)[] Labels = [("Go to definition", "Close"), ("Definition", "Close"), ("", "")];

    /// <summary>Shortens the buttons' labels, down to just their keys, until both fit a box <paramref name="width"/> wide.</summary>
    public void FitButtons(int width)
    {
        foreach (var (goTo, close) in Labels)
        {
            _goTo.Text = $" {KeyedButton.Caption(_scopeKeybindings, CommandIds.QuickInfoGoToDefinition, goTo).TrimEnd()} ";
            _close.Text = $" {KeyedButton.Caption(_scopeKeybindings, CommandIds.QuickInfoClose, close).TrimEnd()} ";
            if (ButtonsWidth <= width) break;
        }
        _goTo.X = Pos.Left(_close) - _goTo.Text.GetColumns() - 2;
    }

    private Button Keyed(ButtonKind kind, string commandId)
    {
        var button = KeyedButton.Of(kind, _scopeKeybindings, commandId, "");
        button.CanFocus = false;
        button.Accepting += (_, e) =>
        {
            e.Handled = true;
            _scopeCommands.TryExecute(commandId);
        };
        return button;
    }

    private int PageHeight => Math.Max(Viewport.Height - 1, 1);

    private void ScrollBy(int rows)
    {
        var top = Math.Clamp(Viewport.Y + rows, 0, Math.Max(_info.Rows.Count - Viewport.Height, 0));
        if (top != Viewport.Y) Viewport = Viewport with { Y = top };
    }

    protected override bool OnDrawingContent(DrawContext? context)
    {
        var normal = GetAttributeForRole(VisualRole.Normal);
        _code.BeginDraw();
        var lexing = false;
        for (var row = 0; row < Viewport.Height; row++)
        {
            var index = Viewport.Y + row;
            SetAttribute(normal);
            Move(0, row);
            AddStr(new string(' ', Viewport.Width));
            if (index >= _info.Rows.Count) continue;
            var line = _info.Rows[index];
            if (!line.IsCode)
            {
                Move(1, row);
                AddStr(line.Text);
                continue;
            }
            var block = _info.Blocks[line.Block];
            lexing |= _code.Draw(this, line.Block, () => _language(block.Language), block.Lines, line.Line, line.Text, line.Offset, 1, row, normal);
        }
        if (lexing) App?.Invoke(SetNeedsDraw);
        return true;
    }
}
