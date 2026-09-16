using TuiCode.Abstractions;
using TuiCode.Syntax;
using TuiCode.Workbench.Grammars;

namespace TuiCode.Workbench.Settings;

/// <summary>The "Grammars" settings panel (#21). Edits stay in <see cref="CurrentAssociations"/> until <see cref="SettingsView"/> saves.</summary>
public sealed class GrammarAssociationsView : View
{
    private const string FooterText = "Enter: change grammar   Delete: reset to default   Type a pattern (.ext or file name) to add";

    private readonly SyntaxHighlighter? _syntax;
    private readonly IInputScopeStack _scopes;
    private readonly Dictionary<string, string> _associations;
    private readonly TextField _search;
    private readonly ListView _list;
    private IReadOnlyList<GrammarAssociationRow> _rows = [];
    private GrammarPickerView? _picker;

    public GrammarAssociationsView(SyntaxHighlighter? syntax, IReadOnlyDictionary<string, string> associations, IInputScopeStack scopes)
    {
        _syntax = syntax;
        _scopes = scopes;
        _associations = new Dictionary<string, string>(associations, StringComparer.OrdinalIgnoreCase);

        X = 0;
        Y = 0;
        Width = Dim.Fill();
        Height = Dim.Fill();
        CanFocus = true;

        _search = new TextField { X = 0, Y = 0, Width = Dim.Fill(), Height = 1 };
        _search.TextChanged += (_, _) => Rebuild();
        _search.KeyDown += OnSearchKey;
        _search.MouseEvent += (_, _) => _search.SetFocus();

        _list = new ListView { X = 0, Y = Pos.Bottom(_search) + 1, Width = Dim.Fill(), Height = Dim.Fill(3) };
        _list.KeyDown += OnListKey;
        _list.MouseEvent += (_, _) => _list.SetFocus();

        var footer = new Label
        {
            X = 0,
            Y = Pos.AnchorEnd(1),
            Text = syntax is null ? "Syntax highlighting isn't available." : FooterText,
        };

        Add(_search, _list, new Label { X = 0, Y = Pos.AnchorEnd(2), Text = UserGrammarsText(syntax) }, footer);
        Rebuild();
    }

    public IReadOnlyDictionary<string, string> CurrentAssociations => _associations;

    internal static string UserGrammarsText(SyntaxHighlighter? syntax) => syntax?.Problems switch
    {
        null => "",
        { Count: 0 } => "More grammars: add VS Code grammar packages to ~/.tui/grammars and restart",
        { Count: 1 } problems => $"~/.tui/grammars: {problems[0]}",
        var problems => $"~/.tui/grammars: {problems[0]} (+{problems.Count - 1} more)",
    };

    public bool FocusContent() => _search.SetFocus();

    private void Rebuild()
    {
        var selected = _list.SelectedItem is { } i && i < _rows.Count ? _rows[i].Pattern : null;
        _rows = _syntax is null
            ? []
            : GrammarAssociationRows.Build(_syntax.DefaultAssociations, _associations, _syntax.LanguageById, _search.Text ?? "");
        _list.Source = new ListWrapper<string>(new(_rows.Select(r => r.Display)));
        if (_rows.Count == 0) return;
        var index = _rows.ToList().FindIndex(r => string.Equals(r.Pattern, selected, StringComparison.OrdinalIgnoreCase));
        _list.SelectedItem = Math.Max(index, 0);
    }

    private void OnSearchKey(object? sender, Key key)
    {
        if (key == Key.Enter)
        {
            EditSelected();
            key.Handled = true;
        }
        else if (key == Key.CursorDown)
        {
            _list.SetFocus();
            key.Handled = true;
        }
    }

    private void OnListKey(object? sender, Key key)
    {
        if (key == Key.Enter)
        {
            EditSelected();
            key.Handled = true;
        }
        else if (key == Key.Delete || key == Key.Backspace)
        {
            ResetSelected();
            key.Handled = true;
        }
        else if (key == Key.CursorLeft && SuperView is SettingsView settings)
        {
            settings.FocusCategories();
            key.Handled = true;
        }
    }

    private GrammarAssociationRow? SelectedRow =>
        _list.SelectedItem is { } i && i >= 0 && i < _rows.Count ? _rows[i] : null;

    private void EditSelected()
    {
        if (_syntax is null || _picker is not null || SelectedRow is not { } row) return;

        var current = _associations.TryGetValue(row.Pattern, out var id)
            ? _syntax.LanguageById(id)
            : _syntax.DefaultAssociations.GetValueOrDefault(row.Pattern);
        var picker = new GrammarPickerView(_syntax.Languages, $"Grammar for {row.Pattern}", current);
        picker.Chosen += (_, grammar) => SetAssociation(row.Pattern, grammar);
        picker.Closed += (_, _) => ClosePicker(picker);

        // Hosted by the settings window rather than this narrower pane, so the picker isn't clipped.
        var host = SuperView ?? this;
        _picker = picker;
        host.Add(picker);
        _scopes.Push(picker.Scope);
        picker.FocusSearch();
    }

    private void ClosePicker(GrammarPickerView picker)
    {
        _scopes.Pop(picker.Scope);
        picker.SuperView?.Remove(picker);
        picker.Dispose();
        _picker = null;
        _list.SetFocus();
    }

    internal void SetAssociation(string pattern, SyntaxLanguage? grammar)
    {
        if (grammar is not null && _syntax?.DefaultAssociations.GetValueOrDefault(pattern)?.Id == grammar.Id)
            _associations.Remove(pattern);
        else
            _associations[pattern] = grammar?.Id ?? SyntaxHighlighter.PlainText;

        _search.Text = "";
        Rebuild();
        SelectPattern(pattern);
    }

    private void ResetSelected()
    {
        if (SelectedRow is not { } row || !_associations.Remove(row.Pattern)) return;
        Rebuild();
        SelectPattern(row.Pattern);
    }

    private void SelectPattern(string pattern)
    {
        var index = _rows.ToList().FindIndex(r => string.Equals(r.Pattern, pattern, StringComparison.OrdinalIgnoreCase));
        if (index >= 0) _list.SelectedItem = index;
    }
}
