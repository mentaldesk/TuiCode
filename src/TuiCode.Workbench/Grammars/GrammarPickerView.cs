using TuiCode.Abstractions;
using TuiCode.Syntax;
using TuiCode.Workbench.Services;

namespace TuiCode.Workbench.Grammars;

/// <summary>Modal grammar list with a filter box, shared by <c>cg</c> and the Grammars settings pane. The owner pushes <see cref="Scope"/> and removes it on <see cref="Closed"/>.</summary>
public sealed class GrammarPickerView : Window
{
    private readonly TextField _search;
    private readonly ListView _list;
    private readonly IReadOnlyList<Row> _allRows;
    private readonly ICommandService _scopeCommands = new CommandService();
    private List<Row> _visibleRows = [];

    public IKeybindingService Scope { get; }

    /// <summary>Raised with the chosen grammar (null for Plain Text).</summary>
    public event EventHandler<SyntaxLanguage?>? Chosen;

    /// <summary>Raised when the picker should be removed: after a choice, or on Esc.</summary>
    public event EventHandler? Closed;

    public GrammarPickerView(IEnumerable<SyntaxLanguage> grammars, string title, SyntaxLanguage? current)
    {
        Title = title;
        BorderStyle = LineStyle.Single;
        X = Pos.Center();
        Y = Pos.Center();
        Width = 60;
        Height = 22;
        CanFocus = true;

        _search = new TextField { X = 1, Y = 0, Width = Dim.Fill(1), Height = 1 };
        _search.TextChanged += (_, _) => RebuildVisible();
        _search.MouseEvent += (_, _) => _search.SetFocus();

        _list = new ListView { X = 1, Y = Pos.Bottom(_search) + 1, Width = Dim.Fill(1), Height = Dim.Fill(1) };
        _list.MouseEvent += (_, _) => _list.SetFocus();

        Add(_search, _list);

        _allRows =
        [
            new Row(null, Workbench.PlainTextName, current is null),
            .. grammars
                .OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
                .Select(g => new Row(g, g.Name, g.Id == current?.Id)),
        ];

        Scope = new KeybindingService(_scopeCommands);
        _scopeCommands.Register(CommandIds.GrammarPickerCancel, () => Closed?.Invoke(this, EventArgs.Empty));
        _scopeCommands.Register(CommandIds.GrammarPickerConfirm, ChooseSelected);
        Scope.Bind("Esc", CommandIds.GrammarPickerCancel);
        Scope.Bind("Enter", CommandIds.GrammarPickerConfirm);

        RebuildVisible();
    }

    public bool FocusSearch() => _search.SetFocus();

    private void RebuildVisible()
    {
        var query = _search.Text ?? "";
        _visibleRows = _allRows
            .Where(r => r.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                        || (r.Grammar?.Id.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false))
            .ToList();

        _list.Source = new ListWrapper<string>(new(_visibleRows.Select(r => (r.IsCurrent ? "● " : "  ") + r.Name)));
        var current = string.IsNullOrEmpty(query) ? _visibleRows.FindIndex(r => r.IsCurrent) : -1;
        _list.SelectedItem = _visibleRows.Count == 0 ? null : Math.Max(current, 0);
    }

    private void ChooseSelected()
    {
        var i = _list.SelectedItem ?? -1;
        if (i < 0 || i >= _visibleRows.Count) return;
        Chosen?.Invoke(this, _visibleRows[i].Grammar);
        Closed?.Invoke(this, EventArgs.Empty);
    }

    private sealed record Row(SyntaxLanguage? Grammar, string Name, bool IsCurrent);
}
