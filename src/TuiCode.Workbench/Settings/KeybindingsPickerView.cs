using Terminal.Gui.Drivers;
using TuiCode.Abstractions;
using TuiCode.Workbench.Services;

namespace TuiCode.Workbench.Settings;

/// <summary>
/// Right-pane panel for the "Keyboard Shortcuts" category. Shows one row per
/// (command, binding) pair, with the command's scope, and a search box on top. Edit gestures:
/// <list type="bullet">
/// <item><c>Enter</c> on a row — start key capture, adds a new binding to that command.</item>
/// <item><c>Delete</c> / <c>Backspace</c> on a row with a binding — remove it.</item>
/// </list>
/// All edits stay in <see cref="CurrentBindings"/> (commit-on-OK only); the live workbench
/// keybinding service is untouched until the owning <see cref="SettingsView"/> saves.
/// </summary>
public sealed class KeybindingsPickerView : View
{
    private readonly IInputScopeStack _scopes;
    private readonly CommandDescriptor[] _commands;
    private readonly Dictionary<string, string> _commandLabels;        // id → label
    private readonly Dictionary<string, CommandScope> _commandScopes;
    private readonly List<KeyBinding> _currentBindings;                 // mutable; reflects the picker's pending state
    private readonly TextField _search;
    private readonly Label _header;
    private readonly ListView _list;
    private readonly Label _footer;

    private IReadOnlyList<KeybindingRow> _displayRows = [];
    private KeyCaptureScope? _activeCapture;
    private string? _capturingForCommand;
    private List<Key> _capturedKeys = new();

    public IReadOnlyList<KeyBinding> CurrentBindings => _currentBindings;

    public KeybindingsPickerView(
        ICommandService commands,
        IKeybindingService initialBindings,
        IInputScopeStack scopes)
    {
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(initialBindings);
        ArgumentNullException.ThrowIfNull(scopes);

        _scopes = scopes;
        _commands = commands.Registered.ToArray();
        _commandLabels = _commands.ToDictionary(c => c.Id, c => c.Label, StringComparer.Ordinal);
        _commandScopes = _commands.ToDictionary(c => c.Id, c => c.Scope, StringComparer.Ordinal);
        _currentBindings = initialBindings.Bindings.ToList();

        X = 0;
        Y = 0;
        Width = Dim.Fill();
        Height = Dim.Fill();
        // Required: TG only allows focus on a descendant if every ancestor has CanFocus = true.
        // Without this, _list.SetFocus() returns false and clicks don't transfer focus into the picker.
        CanFocus = true;

        _search = new TextField
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = 1
        };
        _search.TextChanged += (_, _) => RebuildRows();

        _header = new Label
        {
            X = 0,
            Y = Pos.Bottom(_search) + 1,
            Width = Dim.Fill(),
            Height = 1
        };

        _list = new ListView
        {
            X = 0,
            Y = Pos.Bottom(_header),
            Width = Dim.Fill(),
            Height = Dim.Fill(2)
        };
        _list.KeyDown += OnListKey;
        _list.RowRender += (_, e) =>
            e.RowAttribute = RowStripes.For(e.Row, _list.SelectedItem, _list.GetAttributeForRole(VisualRole.Normal));
        // TG doesn't auto-transfer focus on mouse click in this layout. Force it on any mouse event.
        _list.MouseEvent += (_, _) => _list.SetFocus();
        _search.MouseEvent += (_, _) => _search.SetFocus();

        _footer = new Label
        {
            X = 0,
            Y = Pos.AnchorEnd(1),
            Text = "Enter: add binding   Delete: remove   Type to filter"
        };

        Add(_search, _header, _list, _footer);
        RebuildRows();
    }

    public bool FocusContent() => _list.SetFocus();

    protected override void OnSubViewsLaidOut(LayoutEventArgs args)
    {
        base.OnSubViewsLaidOut(args);
        var header = KeybindingRows.Header(_list.Viewport.Width);
        if (_header.Text != header)
            _header.Text = header;
    }

    /// <summary>Hook to detect whether the picker is mid-capture (so the parent can suppress its own Esc handling).</summary>
    public bool IsCapturing => _activeCapture is not null;

    private void RebuildRows()
    {
        var rows = KeybindingRows.Build(_commands, _currentBindings, _search.Text?.ToString() ?? "");
        _displayRows = rows;
        _list.Source = new KeybindingListSource(rows);
        if (rows.Count > 0 && (_list.SelectedItem is null || _list.SelectedItem >= rows.Count))
            _list.SelectedItem = 0;
    }

    private void OnListKey(object? sender, Key key)
    {
        if (_activeCapture is not null)
            return; // capture scope handles all keys; the list shouldn't see them

        if (key == Key.Enter)
        {
            BeginCapture();
            key.Handled = true;
        }
        else if (key == Key.Delete || key == Key.Backspace)
        {
            RemoveSelected();
            key.Handled = true;
        }
        else if (key == Key.CursorLeft && SuperView is SettingsView settings)
        {
            settings.FocusCategories();
            key.Handled = true;
        }
    }

    private void BeginCapture()
    {
        var i = _list.SelectedItem ?? -1;
        if (i < 0 || i >= _displayRows.Count) return;
        var row = _displayRows[i];

        _capturingForCommand = row.CommandId;
        _capturedKeys = new List<Key>();
        UpdateCaptureFooter();

        _activeCapture = new KeyCaptureScope(OnCaptureKey);
        _scopes.Push(_activeCapture);
    }

    private void OnCaptureKey(Key key)
    {
        // Esc always cancels capture (v1 limitation: can't bind Esc through the UI).
        if (key == Key.Esc)
        {
            EndCapture(commit: false);
            return;
        }

        // Enter commits if we already have a captured chord; otherwise it's recorded as the first key
        // (so a single-key Enter binding remains theoretically reachable: Enter then Enter).
        if (key == Key.Enter && _capturedKeys.Count > 0)
        {
            EndCapture(commit: true);
            return;
        }

        // TG fires a KeyDown for each modifier press (Ctrl alone, then Ctrl+Shift, then
        // Ctrl+Alt+Shift, …) before the real letter arrives. Skip those — only record
        // a key once a non-modifier component is present.
        if (IsModifierOnly(key)) return;

        if (_capturedKeys.Count == 0 && TypesText(key))
        {
            _footer.Text = $"{key} types text; start with Ctrl, Alt or a function key   Esc: cancel";
            return;
        }

        _capturedKeys.Add(key);
        UpdateCaptureFooter();
    }

    private static bool IsModifierOnly(Key key)
    {
        var bare = key.KeyCode & ~(KeyCode.CtrlMask | KeyCode.AltMask | KeyCode.ShiftMask);
        return bare == KeyCode.Null;
    }

    internal static bool TypesText(Key key) =>
        !key.IsCtrl && !key.IsAlt && key.TryGetPrintableRune(out _);

    private void EndCapture(bool commit)
    {
        if (_activeCapture is null) return;
        _scopes.Pop(_activeCapture);
        _activeCapture = null;

        if (!commit || _capturingForCommand is null || _capturedKeys.Count == 0)
        {
            _capturingForCommand = null;
            _capturedKeys = new();
            _footer.Text = "Enter: add binding   Delete: remove   Type to filter";
            return;
        }

        // The captured keys ARE the canonical chord — we never round-trip them through a display
        // string, so an un-parseable chord (e.g. Ctrl+Alt+Shift++) survives the picker intact (#89).
        var candidate = new KeyBinding(_capturedKeys.ToArray(), _capturingForCommand, _commandScopes[_capturingForCommand]);
        _capturingForCommand = null;
        _capturedKeys = new();
        _footer.Text = "Enter: add binding   Delete: remove   Type to filter";

        TryAddBinding(candidate);
    }

    private void UpdateCaptureFooter()
    {
        var captured = _capturedKeys.Count == 0
            ? "(none yet)"
            : string.Join(" ", _capturedKeys.Select(k => k.ToString()));
        _footer.Text = $"Press desired keys: {captured}   Enter: confirm   Esc: cancel";
    }

    private void TryAddBinding(KeyBinding candidate)
    {
        var clash = KeybindingClash.Find(_currentBindings, candidate);
        if (clash is null)
        {
            AddBinding(candidate);
            return;
        }

        var host = SuperView ?? this;
        var message = clash.Message(id => _commandLabels.GetValueOrDefault(id, id));
        if (clash.IsWarning)
        {
            KeybindingConflictDialog.Confirm(host, _scopes, message, "Bind", bind =>
            {
                _list.SetFocus();
                if (bind) AddBinding(candidate);
            });
        }
        else if (clash.Kind is KeybindingConflict.ExactMatch)
        {
            KeybindingConflictDialog.Confirm(host, _scopes, message, "Replace", replace =>
            {
                _list.SetFocus();
                if (!replace) return;
                _currentBindings.Remove(clash.Existing);
                AddBinding(candidate);
            });
        }
        else
        {
            KeybindingConflictDialog.Refuse(host, _scopes, message, () => _list.SetFocus());
        }
    }

    private void AddBinding(KeyBinding binding)
    {
        _currentBindings.Add(binding);
        RebuildRows();
    }

    private void RemoveSelected()
    {
        var i = _list.SelectedItem ?? -1;
        if (i < 0 || i >= _displayRows.Count) return;
        var row = _displayRows[i];
        if (row.Binding is null) return;
        _currentBindings.RemoveAll(b => b.Equals(row.Binding));
        RebuildRows();
    }
}
