using TuiCode.Abstractions;
using TuiCode.Workbench.Services;

namespace TuiCode.Workbench.Help;

public sealed record HelpRow(string Key, string Description);

public sealed record HelpColumn(string Title, IReadOnlyList<HelpRow> Rows);

public sealed class HelpView : Window
{
    internal static readonly HelpColumn Everywhere = new("Everywhere",
    [
        new("Ctrl+E", "Command palette: what applies here"),
        new("Ctrl+Space", "Run a command by its mnemonic"),
        new("Ctrl+O", "Open file or folder"),
        new("Ctrl+N", "New file or folder"),
        new("Ctrl+S", "Save"),
        new("Ctrl+W", "Close tab"),
        new("Ctrl+F", "Find in file"),
        new("Ctrl+Q", "Quit"),
    ]);

    private readonly ICommandService _scopeCommands;
    private readonly IKeybindingService _scopeKeybindings;

    public IKeybindingService Scope => _scopeKeybindings;

    /// <summary>The keys of the place F1 was pressed in, beside <see cref="Everywhere"/>; null when it has none.</summary>
    internal HelpColumn? Place { get; }

    internal bool IsStacked { get; }

    public event EventHandler? Closed;

    public HelpView(HelpColumn? place = null, int availableWidth = int.MaxValue)
    {
        Place = place;
        Title = "Help";
        BorderStyle = LineStyle.Single;
        X = Pos.Center();
        Y = Pos.Center();
        CanFocus = true;

        var (everywhere, leftWidth, leftHeight) = ColumnView(Everywhere);
        everywhere.X = 1;
        int innerWidth, columnsHeight;
        if (place is null)
        {
            Add(everywhere);
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
                Add(here, everywhere);
                innerWidth = Math.Max(leftWidth, rightWidth) + 2;
                columnsHeight = rightHeight + 1 + leftHeight;
            }
            else
            {
                columnsHeight = Math.Max(leftHeight, rightHeight);
                var divider = new Line { Orientation = Orientation.Vertical, X = leftWidth + 2, Y = 0, Height = columnsHeight };
                here.X = leftWidth + 4;
                Add(everywhere, divider, here);
            }
        }
        Width = innerWidth + 2;
        Height = columnsHeight + 4;

        var footer = new Label
        {
            X = Pos.Center(),
            Y = Pos.AnchorEnd(1),
            Text = "Esc · Enter  close",
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
