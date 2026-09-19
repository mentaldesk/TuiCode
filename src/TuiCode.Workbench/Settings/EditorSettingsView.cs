using TuiCode.Abstractions;

namespace TuiCode.Workbench.Settings;

/// <summary>Settings → Editor (#14). <see cref="SettingsView"/> applies <see cref="Current"/> on save.</summary>
public sealed class EditorSettingsView : View
{
    private const int ControlColumn = 15;

    private readonly EditorSettings _original;
    private readonly NumericUpDown<int> _indentSize;
    private readonly CheckBox _insertSpaces;
    private readonly OptionSelector<LineEnding> _lineEnding;
    private readonly CheckBox _insertFinalNewline;

    public EditorSettings Current => _original with
    {
        IndentSize = _indentSize.Value,
        InsertSpaces = _insertSpaces.Value == CheckState.Checked,
        LineEnding = _lineEnding.Value ?? LineEnding.Auto,
        InsertFinalNewline = _insertFinalNewline.Value == CheckState.Checked,
    };

    public EditorSettingsView(EditorSettings settings)
    {
        _original = settings;
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

        Add(indentSizeLabel, _indentSize, _insertSpaces, lineEndingLabel, _lineEnding, _insertFinalNewline);
        KeyDown += OnKey;
    }

    public bool FocusContent() => _indentSize.SetFocus();

    private static CheckState Check(bool value) => value ? CheckState.Checked : CheckState.UnChecked;

    private void OnKey(object? sender, Key key)
    {
        if (key == Key.CursorLeft && SuperView is SettingsView settings)
        {
            settings.FocusCategories();
            key.Handled = true;
        }
    }
}
