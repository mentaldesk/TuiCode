using TuiCode.Abstractions;
using TuiCode.Syntax;
using TuiCode.Workbench.Languages;

namespace TuiCode.Workbench.Settings;

/// <summary>Settings › Language Servers (#456). Choices stay in <see cref="Current"/> until <see cref="SettingsView"/> saves.</summary>
public sealed class LanguageServersView : View
{
    internal const string FooterText = "Enter: choose server   Delete: reset to default";

    private readonly IReadOnlyCollection<SyntaxLanguage> _languages;
    private readonly Dictionary<string, LanguageServerSetting> _chosen;
    private readonly Func<string, bool> _isInstalled;
    private readonly IInputScopeStack _scopes;
    private readonly Label _header;
    private readonly ListView _list;
    private IReadOnlyList<LanguageServerRow> _rows = [];

    public LanguageServersView(
        SyntaxHighlighter? syntax,
        IReadOnlyDictionary<string, LanguageServerSetting> chosen,
        Func<string, bool> isInstalled,
        IInputScopeStack scopes)
    {
        _languages = syntax?.Languages ?? [];
        _chosen = new Dictionary<string, LanguageServerSetting>(chosen, StringComparer.OrdinalIgnoreCase);
        _isInstalled = isInstalled;
        _scopes = scopes;

        X = 0;
        Y = 0;
        Width = Dim.Fill();
        Height = Dim.Fill();
        CanFocus = true;

        _header = new Label { X = 0, Y = 0, Width = Dim.Fill() };
        _list = new ListView { X = 0, Y = 1, Width = Dim.Fill(), Height = Dim.Fill(2) };
        _list.KeyDown += OnListKey;
        _list.MouseEvent += (_, _) => _list.SetFocus();
        var footer = new Label { X = 0, Y = Pos.AnchorEnd(1), Text = FooterText };

        Add(_header, _list, footer);
        Rebuild();
    }

    public IReadOnlyDictionary<string, LanguageServerSetting> Current => _chosen;

    internal IReadOnlyList<LanguageServerRow> Rows => _rows;

    internal LanguageServerDialog? Dialog { get; private set; }

    public bool FocusContent() => _list.SetFocus();

    private LanguageServerRow? SelectedRow =>
        _list.SelectedItem is { } i && i >= 0 && i < _rows.Count ? _rows[i] : null;

    private void Rebuild(string? select = null)
    {
        select ??= SelectedRow?.Language.Id;
        _rows = LanguageServerRows.Build(_languages, _chosen, _isInstalled);
        _header.Text = LanguageServerRows.Header(_rows);
        _list.Source = new ListWrapper<string>(new(_rows.Select(row => LanguageServerRows.Display(_rows, row))));
        if (_rows.Count == 0) return;
        var index = _rows.ToList().FindIndex(r => string.Equals(r.Language.Id, select, StringComparison.OrdinalIgnoreCase));
        _list.SelectedItem = Math.Max(index, 0);
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

    internal void EditSelected()
    {
        if (Dialog is not null || SelectedRow is not { } row) return;
        var dialog = new LanguageServerDialog(row.Language, row.Setting, _isInstalled);
        dialog.Saved += (_, setting) => Choose(row.Language.Id, setting);
        dialog.Closed += (_, _) => CloseDialog(dialog);

        // Hosted by the settings window rather than this narrower pane, so the dialog isn't clipped.
        var host = SuperView ?? this;
        Dialog = dialog;
        host.Add(dialog);
        _scopes.Push(dialog.Scope);
        dialog.FocusServer();
    }

    private void CloseDialog(LanguageServerDialog dialog)
    {
        _scopes.Pop(dialog.Scope);
        dialog.SuperView?.Remove(dialog);
        dialog.Dispose();
        Dialog = null;
        _list.SetFocus();
    }

    internal void Choose(string languageId, LanguageServerSetting setting)
    {
        if (setting == KnownLanguageServers.Default(languageId))
            _chosen.Remove(languageId);
        else
            _chosen[languageId] = setting;
        Rebuild(languageId);
    }

    internal void ResetSelected()
    {
        if (SelectedRow is not { } row || !_chosen.Remove(row.Language.Id)) return;
        Rebuild(row.Language.Id);
    }
}
