using TuiCode.Abstractions;

namespace TuiCode.Workbench.Settings;

/// <summary>Settings → Editor (#14). Enter or Space steps the selected setting to its next value; <see cref="SettingsView"/> applies them on save.</summary>
public sealed class EditorSettingsView : View
{
    private static readonly LineEnding[] LineEndings = [LineEnding.Auto, LineEnding.LF, LineEnding.CRLF];

    private readonly ListView _list;

    public EditorSettings Current { get; private set; }

    public EditorSettingsView(EditorSettings settings)
    {
        Current = settings;
        X = 0;
        Y = 0;
        Width = Dim.Fill();
        Height = Dim.Fill();
        // Required: TG only allows focus on a descendant if every ancestor has CanFocus = true.
        CanFocus = true;

        _list = new ListView
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = 4,
        };
        _list.KeyDown += OnListKey;
        // TG doesn't auto-transfer focus on mouse click in this layout. Force it on any mouse event.
        _list.MouseEvent += (_, _) => _list.SetFocus();

        var hint = new Label
        {
            X = 0,
            Y = Pos.Bottom(_list) + 1,
            Width = Dim.Fill(),
            Text = "Enter or Space: change the selected setting",
        };

        Add(_list, hint);
        ShowRows();
    }

    public bool FocusContent() => _list.SetFocus();

    private void ShowRows()
    {
        var selected = _list.SelectedItem ?? 0;
        _list.Source = new ListWrapper<string>(new(
        [
            $"Indent size           {Current.IndentSize}",
            $"Indent with           {(Current.InsertSpaces ? "Spaces" : "Tabs")}",
            $"Line endings          {Current.LineEnding switch { LineEnding.Auto => "Keep each file's", var e => e.ToString() }}",
            $"Insert final newline  {(Current.InsertFinalNewline ? "On" : "Off")}",
        ]));
        _list.SelectedItem = selected;
    }

    private void Step(int row)
    {
        Current = row switch
        {
            0 => Current with { IndentSize = Current.IndentSize % EditorSettings.MaxIndentSize + 1 },
            1 => Current with { InsertSpaces = !Current.InsertSpaces },
            2 => Current with { LineEnding = LineEndings[(Array.IndexOf(LineEndings, Current.LineEnding) + 1) % LineEndings.Length] },
            3 => Current with { InsertFinalNewline = !Current.InsertFinalNewline },
            _ => Current,
        };
        ShowRows();
    }

    private void OnListKey(object? sender, Key key)
    {
        if (key == Key.CursorLeft && SuperView is SettingsView settings)
        {
            settings.FocusCategories();
            key.Handled = true;
        }
        else if ((key == Key.Enter || key == Key.Space) && _list.SelectedItem is { } row)
        {
            Step(row);
            key.Handled = true;
        }
    }
}
