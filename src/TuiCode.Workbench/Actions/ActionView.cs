using TuiCode.Abstractions;
using TuiCode.Workbench.Services;

namespace TuiCode.Workbench.Actions;

/// <summary>
/// Modal command-palette overlay (VS Code's F1). Lists every registered
/// command with its current key bindings; typing filters; Enter executes the
/// highlighted command and closes; Esc cancels.
///
/// Owns its own <see cref="ICommandService"/> + <see cref="IKeybindingService"/>
/// for the modal scope. The owning <see cref="WorkbenchHost"/> pushes
/// <see cref="Scope"/> on the input scope stack on open and pops on
/// <see cref="Closed"/>.
/// </summary>
public sealed class ActionView : Window
{
    private readonly Action<string> _execute;
    private readonly TextField _search;
    private readonly ListView _list;

    private readonly ICommandService _scopeCommands;
    private readonly IKeybindingService _scopeKeybindings;

    private List<ActionRow> _allRows;
    private List<ActionRow> _visibleRows;

    public IKeybindingService Scope => _scopeKeybindings;

    /// <summary>Fired after the view wants to be removed (Esc or after a command was dispatched).</summary>
    public event EventHandler? Closed;

    public ActionView(
        ICommandService workbenchCommands,
        IKeybindingService workbenchKeybindings,
        Action<string> execute)
    {
        ArgumentNullException.ThrowIfNull(workbenchCommands);
        ArgumentNullException.ThrowIfNull(workbenchKeybindings);
        ArgumentNullException.ThrowIfNull(execute);

        _execute = execute;

        Title = "Actions";
        BorderStyle = LineStyle.Single;
        X = Pos.Center();
        Y = Pos.Center();
        Width = 76;
        Height = 22;
        // Required for descendant focus — same reason as KeybindingsPickerView.
        CanFocus = true;

        _search = new TextField
        {
            X = 1,
            Y = 0,
            Width = Dim.Fill(1),
            Height = 1,
        };
        _search.TextChanged += (_, _) => RebuildVisible();
        _search.MouseEvent += (_, _) => _search.SetFocus();

        _list = new ListView
        {
            X = 1,
            Y = Pos.Bottom(_search) + 1,
            Width = Dim.Fill(1),
            Height = Dim.Fill(1),
        };
        _list.MouseEvent += (_, _) => _list.SetFocus();

        Add(_search, _list);

        _scopeCommands = new CommandService();
        _scopeKeybindings = new KeybindingService(_scopeCommands);
        RegisterScopeBindings();

        _allRows = BuildRows(workbenchCommands, workbenchKeybindings);
        _visibleRows = _allRows;
        RebuildVisible();
    }

    public bool FocusSearch() => _search.SetFocus();

    private static List<ActionRow> BuildRows(ICommandService commands, IKeybindingService keybindings)
    {
        var bindingsByCommand = keybindings.Bindings
            .GroupBy(b => b.CommandId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(b => b.Display).ToArray(), StringComparer.Ordinal);

        return commands.Registered
            .Select(c => new ActionRow(
                c.Id,
                c.Label,
                bindingsByCommand.TryGetValue(c.Id, out var seqs) ? string.Join(", ", seqs) : ""))
            .OrderBy(r => r.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void RebuildVisible()
    {
        var query = _search.Text?.ToString() ?? "";
        _visibleRows = string.IsNullOrWhiteSpace(query)
            ? _allRows
            : _allRows.Where(r => Matches(r, query)).ToList();

        var lines = _visibleRows.Select(FormatRow).ToList();
        _list.Source = new ListWrapper<string>(new(lines));
        _list.SelectedItem = _visibleRows.Count > 0 ? 0 : null;
    }

    private static bool Matches(ActionRow r, string needle) =>
        r.Label.Contains(needle, StringComparison.OrdinalIgnoreCase)
        || r.Bindings.Contains(needle, StringComparison.OrdinalIgnoreCase)
        || r.CommandId.Contains(needle, StringComparison.OrdinalIgnoreCase);

    private static string FormatRow(ActionRow r) =>
        $"{Truncate(r.Label, 48).PadRight(50)}{r.Bindings}";

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..(max - 1)] + "…";

    private void RegisterScopeBindings()
    {
        _scopeCommands.Register(CommandIds.ActionsCancel, () => Closed?.Invoke(this, EventArgs.Empty));
        _scopeCommands.Register(CommandIds.ActionsExecute, ExecuteSelected);

        _scopeKeybindings.Bind("Esc", CommandIds.ActionsCancel);
        _scopeKeybindings.Bind("Enter", CommandIds.ActionsExecute);
    }

    private void ExecuteSelected()
    {
        var i = _list.SelectedItem ?? -1;
        if (i < 0 || i >= _visibleRows.Count) return;
        var commandId = _visibleRows[i].CommandId;
        Closed?.Invoke(this, EventArgs.Empty);
        _execute(commandId);
    }

    private sealed record ActionRow(string CommandId, string Label, string Bindings);
}
