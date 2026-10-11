using TuiCode.Abstractions;
using TuiCode.Syntax;

namespace TuiCode.Workbench.Settings;

/// <summary>Settings → Editor (#14). <see cref="SettingsView"/> applies <see cref="Current"/> on save.</summary>
public sealed class EditorSettingsView : View
{
    private const int ControlColumn = 15;
    internal const string WrapByLanguageHint = "Enter: On / Off / Default   Delete: reset   Type to add";

    private readonly EditorSettings _original;
    private readonly NumericUpDown<int> _indentSize;
    private readonly CheckBox _insertSpaces;
    private readonly OptionSelector<LineEnding> _lineEnding;
    private readonly CheckBox _insertFinalNewline;
    private readonly CheckBox _wordWrap;
    private readonly CheckBox _stickyLines;
    private readonly IReadOnlyCollection<SyntaxLanguage> _languages;
    private readonly Dictionary<string, bool> _wrapByLanguage;
    private readonly TextField _languageFilter;
    private readonly ListView _languageList;
    private IReadOnlyList<WrapLanguageRow> _languageRows = [];

    public EditorSettings Current => _original with
    {
        IndentSize = _indentSize.Value,
        InsertSpaces = _insertSpaces.Value == CheckState.Checked,
        LineEnding = _lineEnding.Value ?? LineEnding.Auto,
        InsertFinalNewline = _insertFinalNewline.Value == CheckState.Checked,
        WordWrap = _wordWrap.Value == CheckState.Checked,
        StickyLines = _stickyLines.Value == CheckState.Checked,
        WrapByLanguage = new Dictionary<string, bool>(_wrapByLanguage, StringComparer.OrdinalIgnoreCase),
    };

    public EditorSettingsView(EditorSettings settings, SyntaxHighlighter? syntax = null)
    {
        _original = settings;
        _languages = syntax?.Languages ?? [];
        _wrapByLanguage = new Dictionary<string, bool>(settings.WrapByLanguage, StringComparer.OrdinalIgnoreCase);
        X = 0;
        Y = 0;
        Width = Dim.Fill();
        Height = Dim.Fill();
        // Required: TG only allows focus on a descendant if every ancestor has CanFocus = true.
        CanFocus = true;

        var indentSizeLabel = new Label { X = 0, Y = 0, Text = "Indent size" };
        _indentSize = new NumericUpDown<int> { X = ControlColumn, Y = 0, Value = settings.IndentSize };
        _indentSize.ValueChanging += (_, e) =>
            e.Handled = e.NewValue is < EditorSettings.MinIndentSize or > EditorSettings.MaxIndentSize;

        _insertSpaces = new CheckBox { X = 0, Y = 2, Text = "Indent with spaces", Value = Check(settings.InsertSpaces) };

        var lineEndingLabel = new Label { X = 0, Y = 4, Text = "Line endings" };
        _lineEnding = new OptionSelector<LineEnding>
        {
            X = ControlColumn,
            Y = 4,
            Orientation = Orientation.Horizontal,
            TabBehavior = TabBehavior.NoStop,
            Labels = ["Keep each file's", "LF", "CRLF"],
            Value = settings.LineEnding,
        };

        _insertFinalNewline = new CheckBox
        {
            X = 0,
            Y = 6,
            Text = "Insert final newline",
            Value = Check(settings.InsertFinalNewline),
        };

        _wordWrap = new CheckBox { X = 0, Y = 8, Text = "Wrap long lines", Value = Check(settings.WordWrap) };

        _stickyLines = new CheckBox { X = 0, Y = 10, Text = "Show sticky lines", Value = Check(settings.StickyLines) };

        var wrapByLanguageLabel = new Label { X = 0, Y = 12, Text = "Wrap by language" };
        _languageFilter = new TextField { X = 0, Y = 13, Width = Dim.Fill(), Height = 1 };
        _languageFilter.TextChanged += (_, _) => RebuildLanguages();
        _languageFilter.KeyDown += OnLanguageFilterKey;
        _languageFilter.MouseEvent += (_, _) => _languageFilter.SetFocus();
        _languageList = new ListView { X = 0, Y = 14, Width = Dim.Fill(), Height = Dim.Fill(1) };
        _languageList.KeyDown += OnLanguageListKey;
        _languageList.MouseEvent += (_, _) => _languageList.SetFocus();
        var hint = new Label { X = 0, Y = Pos.AnchorEnd(1), Text = WrapByLanguageHint };

        Add(indentSizeLabel, _indentSize, _insertSpaces, lineEndingLabel, _lineEnding, _insertFinalNewline, _wordWrap,
            _stickyLines, wrapByLanguageLabel, _languageFilter, _languageList, hint);
        KeyDown += OnKey;
        RebuildLanguages();
    }

    internal IReadOnlyList<WrapLanguageRow> LanguageRows => _languageRows;

    public bool FocusContent() => _indentSize.SetFocus();

    private static CheckState Check(bool value) => value ? CheckState.Checked : CheckState.UnChecked;

    private WrapLanguageRow? SelectedLanguage =>
        _languageList.SelectedItem is { } i && i >= 0 && i < _languageRows.Count ? _languageRows[i] : null;

    private void RebuildLanguages(string? select = null)
    {
        select ??= SelectedLanguage?.Id;
        _languageRows = WrapLanguageRows.Build(
            EditorSettings.DefaultWrapByLanguage, _wrapByLanguage, _languages, _languageFilter.Text ?? "");
        _languageList.Source = new ListWrapper<string>(new(_languageRows.Select(r => r.Display)));
        if (_languageRows.Count == 0) return;
        var index = _languageRows.ToList().FindIndex(r => string.Equals(r.Id, select, StringComparison.OrdinalIgnoreCase));
        _languageList.SelectedItem = Math.Max(index, 0);
    }

    internal void CycleSelectedLanguage()
    {
        if (SelectedLanguage is not { } row) return;
        var next = row.Kind == WrapLanguageKind.Add
            ? true
            : WrapLanguageRows.Next(_wrapByLanguage.TryGetValue(row.Id, out var wrap) ? wrap : null, row.DefaultWrap);
        if (next is { } value)
            _wrapByLanguage[row.Id] = value;
        else
            _wrapByLanguage.Remove(row.Id);
        RebuildLanguages(row.Id);
    }

    internal void ResetSelectedLanguage()
    {
        if (SelectedLanguage is not { } row || !_wrapByLanguage.Remove(row.Id)) return;
        RebuildLanguages(row.Id);
    }

    private void OnLanguageFilterKey(object? sender, Key key)
    {
        if (key == Key.Enter)
        {
            CycleSelectedLanguage();
            key.Handled = true;
        }
        else if (key == Key.CursorDown)
        {
            _languageList.SetFocus();
            key.Handled = true;
        }
    }

    private void OnLanguageListKey(object? sender, Key key)
    {
        if (key == Key.Enter)
        {
            CycleSelectedLanguage();
            key.Handled = true;
        }
        else if (key == Key.Delete || key == Key.Backspace)
        {
            ResetSelectedLanguage();
            key.Handled = true;
        }
    }

    private void OnKey(object? sender, Key key)
    {
        if (key == Key.CursorLeft && SuperView is SettingsView settings)
        {
            settings.FocusCategories();
            key.Handled = true;
        }
    }
}
