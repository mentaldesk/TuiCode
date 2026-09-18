using TuiCode.Abstractions;
using TuiCode.Icons;

namespace TuiCode.Workbench.Settings;

/// <summary>Settings → File Icons (#119). Applies live, like the theme picker; <see cref="SettingsView"/> reverts on cancel.</summary>
public sealed class FileIconsPickerView : View
{
    private static readonly FileIconStyle[] Styles = [FileIconStyle.Auto, FileIconStyle.NerdFont, FileIconStyle.Emoji, FileIconStyle.Off];
    private static readonly string[] SampleFiles = ["Program.cs", "README.md", "package.json", "Dockerfile"];

    private readonly FileIcons _icons;
    private readonly ListView _list;
    private readonly ListView _sample;

    public FileIconsPickerView(FileIcons icons)
    {
        _icons = icons;
        X = 0;
        Y = 0;
        Width = Dim.Fill();
        Height = Dim.Fill();
        // Required: TG only allows focus on a descendant if every ancestor has CanFocus = true.
        CanFocus = true;

        var detected = icons.Detection.NerdFont ? "Nerd Font" : "Emoji";
        string[] labels = [$"Auto ({detected})", "Nerd Font", "Emoji", "Off"];
        _list = new ListView
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = labels.Length,
            Source = new ListWrapper<string>(new(labels)),
            SelectedItem = Array.IndexOf(Styles, icons.Setting),
        };
        _list.ValueChanged += (_, _) =>
        {
            if (_list.SelectedItem is { } i and >= 0 && i < Styles.Length)
                _icons.Setting = Styles[i];
        };
        _list.KeyDown += OnListKey;
        // TG doesn't auto-transfer focus on mouse click in this layout. Force it on any mouse event.
        _list.MouseEvent += (_, _) => _list.SetFocus();

        var detection = new Label
        {
            X = 0,
            Y = Pos.Bottom(_list) + 1,
            Width = Dim.Fill(),
            Text = $"Auto: {icons.Detection.Reason}.",
        };
        var hint = new Label
        {
            X = 0,
            Y = Pos.Bottom(detection),
            Width = Dim.Fill(),
            Text = "If the icons below show as boxes or question marks, your font has no Nerd Font glyphs.",
        };
        _sample = new ListView
        {
            X = 0,
            Y = Pos.Bottom(hint) + 1,
            Width = Dim.Fill(),
            Height = SampleFiles.Length + 1,
            CanFocus = false,
        };

        Add(_list, detection, hint, _sample);
        ShowSample();
        icons.Changed += OnIconsChanged;
    }

    public bool FocusContent() => _list.SetFocus();

    private void OnIconsChanged(object? sender, EventArgs e) => ShowSample();

    private void ShowSample()
    {
        string[] items = ["src", .. SampleFiles];
        FileIcon?[] icons = [_icons.ForDirectory(false), .. SampleFiles.Select(_icons.ForFile)];
        _sample.Source = new IconListSource(items, icons);
    }

    private void OnListKey(object? sender, Key key)
    {
        if (key == Key.CursorLeft && SuperView is SettingsView settings)
        {
            settings.FocusCategories();
            key.Handled = true;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _icons.Changed -= OnIconsChanged;
        base.Dispose(disposing);
    }
}
