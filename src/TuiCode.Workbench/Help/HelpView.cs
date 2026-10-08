using TuiCode.Abstractions;
using TuiCode.Workbench.Services;

namespace TuiCode.Workbench.Help;

public sealed record HelpRow(string Key, string Description);

public sealed record HelpColumn(string Title, IReadOnlyList<HelpRow> Rows);

public sealed class HelpView : Window
{
    internal static readonly HelpColumn Everywhere = new("Everywhere",
    [
        new("Ctrl+E", "Command palette"),
        new("Ctrl+Space", "Run a command by its mnemonic"),
        new("Ctrl+O", "Open file or folder"),
        new("Ctrl+N", "New file or folder"),
        new("Ctrl+S", "Save"),
        new("Ctrl+W", "Close tab"),
        new("Ctrl+F", "Find in file"),
        new("Ctrl+Q", "Quit"),
    ]);

    private const int Chrome = 4;

    private readonly ICommandService _scopeCommands;
    private readonly IKeybindingService _scopeKeybindings;
    private readonly View _body;

    public IKeybindingService Scope => _scopeKeybindings;

    /// <summary>The keys of the place F1 was pressed in, beside <see cref="Everywhere"/>; null when it has none.</summary>
    internal HelpColumn? Place { get; }

    internal bool IsStacked { get; }

    internal bool IsScrollable { get; }

    internal View Body => _body;

    public event EventHandler? Closed;

    public HelpView(HelpColumn? place = null, int availableWidth = int.MaxValue, int availableHeight = int.MaxValue)
    {
        Place = place;
        Title = "Help";
        BorderStyle = LineStyle.Single;
        X = Pos.Center();
        Y = Pos.Center();
        CanFocus = true;
        _body = new View();

        var (everywhere, leftWidth, leftHeight) = ColumnView(Everywhere);
        everywhere.X = 1;
        int innerWidth, columnsHeight;
        if (place is null)
        {
            _body.Add(everywhere);
            (innerWidth, columnsHeight) = (leftWidth + 2, leftHeight);
        }
        else
        {
            var (here, rightWidth, rightHeight) = ColumnView(place);
            innerWidth = leftWidth + rightWidth + 5;
            IsStacked = innerWidth + 2 > availableWidth;
            if (IsStacked)
            {
                here.X = 1;
                everywhere.Y = rightHeight + 1;
                _body.Add(here, everywhere);
                innerWidth = Math.Max(leftWidth, rightWidth) + 2;
                columnsHeight = rightHeight + 1 + leftHeight;
            }
            else
            {
                columnsHeight = Math.Max(leftHeight, rightHeight);
                var divider = new Line { Orientation = Orientation.Vertical, X = leftWidth + 2, Y = 0, Height = columnsHeight };
                here.X = leftWidth + 4;
                _body.Add(everywhere, divider, here);
            }
        }

        var bodyHeight = Math.Clamp(availableHeight - Chrome, 1, columnsHeight);
        IsScrollable = bodyHeight < columnsHeight;
        var bodyWidth = IsScrollable ? innerWidth + 1 : innerWidth;
        _body.Width = bodyWidth;
        _body.Height = bodyHeight;
        _body.SetContentSize(new System.Drawing.Size(innerWidth, columnsHeight));
        if (IsScrollable) _body.ViewportSettings |= ViewportSettingsFlags.HasScrollBars;
        Add(_body);
        Width = bodyWidth + 2;
        Height = bodyHeight + Chrome;

        var footer = new Label
        {
            X = Pos.Center(),
            Y = Pos.AnchorEnd(1),
            Text = IsScrollable ? "↑ ↓ scroll · Esc · Enter  close" : "Esc · Enter  close",
        };
        Add(footer);

        _scopeCommands = new CommandService();
        _scopeKeybindings = new KeybindingService(_scopeCommands);
        RegisterScopeBindings();
    }

    private void RegisterScopeBindings()
    {
        _scopeCommands.Register(CommandIds.HelpClose, () => Closed?.Invoke(this, EventArgs.Empty));
        _scopeKeybindings.Bind("Esc", CommandIds.HelpClose);
        _scopeKeybindings.Bind("Enter", CommandIds.HelpClose);
        if (!IsScrollable) return;
        _scopeCommands.Register(CommandIds.HelpScrollUp, () => ScrollBy(-1));
        _scopeCommands.Register(CommandIds.HelpScrollDown, () => ScrollBy(1));
        _scopeCommands.Register(CommandIds.HelpPageUp, () => ScrollBy(-_body.Viewport.Height));
        _scopeCommands.Register(CommandIds.HelpPageDown, () => ScrollBy(_body.Viewport.Height));
        _scopeKeybindings.Bind("CursorUp", CommandIds.HelpScrollUp);
        _scopeKeybindings.Bind("CursorDown", CommandIds.HelpScrollDown);
        _scopeKeybindings.Bind("PageUp", CommandIds.HelpPageUp);
        _scopeKeybindings.Bind("PageDown", CommandIds.HelpPageDown);
    }

    private void ScrollBy(int rows)
    {
        var bottom = Math.Max(0, _body.GetContentSize().Height - _body.Viewport.Height);
        _body.Viewport = _body.Viewport with { Y = Math.Clamp(_body.Viewport.Y + rows, 0, bottom) };
    }

    private static (View View, int Width, int Height) ColumnView(HelpColumn column)
    {
        var keyWidth = column.Rows.Max(row => row.Key.Length) + 1;
        var rows = column.Rows.Select(row => row.Key.PadRight(keyWidth) + row.Description).ToList();
        var width = rows.Append(column.Title).Max(text => text.Length);
        var height = rows.Count + 2;
        var view = new View { Width = width, Height = height };
        view.Add(
            new Label { X = 0, Y = 0, Text = column.Title },
            new Line { X = 0, Y = 1, Width = width },
            new Label { X = 0, Y = 2, Width = width, Height = rows.Count, Text = string.Join("\n", rows) });
        return (view, width, height);
    }
}
