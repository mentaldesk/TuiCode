using Terminal.Gui.Drivers;
using TuiCode.Abstractions;

namespace TuiCode.Workbench.Settings;

/// <summary>
/// Right-pane panel for the "Keyboard Shortcuts" category. Shows one row per
/// (command, binding) pair with a search box on top. Edit gestures:
/// <list type="bullet">
/// <item><c>Enter</c> on a row — start key capture, adds a new binding to that command.</item>
/// <item><c>Delete</c> / <c>Backspace</c> on a row with a binding — remove it.</item>
/// </list>
/// All edits stay in <see cref="CurrentBindings"/> (commit-on-OK only); the live workbench
/// keybinding service is untouched until the owning <see cref="SettingsView"/> saves.
/// </summary>
public sealed class KeybindingsPickerView : View
{
    private const string Unbound = "(unbound)";

    private readonly IInputScopeStack _scopes;
    private readonly Dictionary<string, string> _commandLabels;        // id → label
    private readonly List<KeyBinding> _currentBindings;                 // mutable; reflects the picker's pending state
    private readonly TextField _search;
    private readonly ListView _list;
    private readonly Label _footer;

    private List<Row> _displayRows = new();
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
        _commandLabels = commands.Registered.ToDictionary(c => c.Id, c => c.Label, StringComparer.Ordinal);
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

        _list = new ListView
        {
            X = 0,
            Y = Pos.Bottom(_search) + 1,
            Width = Dim.Fill(),
            Height = Dim.Fill(2)
        };
        _list.KeyDown += OnListKey;
        // TG doesn't auto-transfer focus on mouse click in this layout. Force it on any mouse event.
        _list.MouseEvent += (_, _) => _list.SetFocus();
        _search.MouseEvent += (_, _) => _search.SetFocus();

        _footer = new Label
        {
            X = 0,
            Y = Pos.AnchorEnd(1),
            Text = "Enter: add binding   Delete: remove   Type to filter"
        };

        Add(_search, _list, _footer);
        RebuildRows();
    }

    public bool FocusContent() => _list.SetFocus();

    /// <summary>Hook to detect whether the picker is mid-capture (so the parent can suppress its own Esc handling).</summary>
    public bool IsCapturing => _activeCapture is not null;

    private void RebuildRows()
    {
        var search = _search.Text?.ToString() ?? "";

        var rows = new List<Row>();
        foreach (var (id, label) in _commandLabels.OrderBy(kv => kv.Value, StringComparer.OrdinalIgnoreCase))
        {
            var bindings = _currentBindings.Where(b => string.Equals(b.CommandId, id, StringComparison.Ordinal)).ToArray();
            if (bindings.Length == 0)
            {
                rows.Add(new Row(id, label, null));
            }
            else
            {
                foreach (var b in bindings)
                    rows.Add(new Row(id, label, b.Sequence));
            }
        }

        if (!string.IsNullOrWhiteSpace(search))
            rows = rows.Where(r => Matches(r, search)).ToList();

        _displayRows = rows;
        var lines = rows.Select(FormatRow).ToList();
        _list.Source = new ListWrapper<string>(new(lines));
        if (rows.Count > 0 && (_list.SelectedItem is null || _list.SelectedItem >= rows.Count))
            _list.SelectedItem = 0;
    }

    private static bool Matches(Row r, string needle) =>
        r.Label.Contains(needle, StringComparison.OrdinalIgnoreCase)
        || (r.Binding?.Contains(needle, StringComparison.OrdinalIgnoreCase) ?? false);

    private static string FormatRow(Row r) =>
        $"{Truncate(r.Label, 40).PadRight(42)}{r.Binding ?? Unbound}";

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..(max - 1)] + "…";

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

        _capturedKeys.Add(key);
        UpdateCaptureFooter();
    }

    private static bool IsModifierOnly(Key key)
    {
        var bare = key.KeyCode & ~(KeyCode.CtrlMask | KeyCode.AltMask | KeyCode.ShiftMask);
        return bare == KeyCode.Null;
    }

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

        var sequence = string.Join(" ", _capturedKeys.Select(k => k.ToString()));
        var commandId = _capturingForCommand;
        _capturingForCommand = null;
        _capturedKeys = new();
        _footer.Text = "Enter: add binding   Delete: remove   Type to filter";

        TryAddBinding(sequence, commandId);
    }

    private void UpdateCaptureFooter()
    {
        var captured = _capturedKeys.Count == 0
            ? "(none yet)"
            : string.Join(" ", _capturedKeys.Select(k => k.ToString()));
        _footer.Text = $"Press desired keys: {captured}   Enter: confirm   Esc: cancel";
    }

    private void TryAddBinding(string sequence, string commandId)
    {
        var conflict = CheckConflictAgainstCurrent(sequence);
        if (conflict is KeybindingConflict.PrefixOfExisting or KeybindingConflict.ExtensionOfExisting)
        {
            KeybindingConflictDialog.ShowChord(this, _scopes, sequence);
            return;
        }
        if (conflict is KeybindingConflict.ExactMatch)
        {
            var existingCommand = _currentBindings.First(b => string.Equals(b.Sequence, sequence, StringComparison.Ordinal)).CommandId;
            var existingLabel = _commandLabels.GetValueOrDefault(existingCommand, existingCommand);
            KeybindingConflictDialog.ShowReplace(this, _scopes, sequence, existingLabel, replace =>
            {
                if (!replace) return;
                _currentBindings.RemoveAll(b => string.Equals(b.Sequence, sequence, StringComparison.Ordinal));
                _currentBindings.Add(new KeyBinding(sequence, commandId));
                RebuildRows();
            });
            return;
        }

        _currentBindings.Add(new KeyBinding(sequence, commandId));
        RebuildRows();
    }

    /// <summary>
    /// Conflict check against the picker's pending state, not the live keybinding service.
    /// Mirrors <see cref="IKeybindingService.CheckConflict"/> but operates on <see cref="_currentBindings"/>.
    /// </summary>
    private KeybindingConflict? CheckConflictAgainstCurrent(string sequence)
    {
        var sequenceParts = sequence.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        foreach (var b in _currentBindings)
        {
            if (string.Equals(b.Sequence, sequence, StringComparison.Ordinal))
                return KeybindingConflict.ExactMatch;

            var bParts = b.Sequence.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (bParts.Length > sequenceParts.Length && bParts.Take(sequenceParts.Length).SequenceEqual(sequenceParts))
                return KeybindingConflict.PrefixOfExisting;
            if (bParts.Length < sequenceParts.Length && sequenceParts.Take(bParts.Length).SequenceEqual(bParts))
                return KeybindingConflict.ExtensionOfExisting;
        }
        return null;
    }

    private void RemoveSelected()
    {
        var i = _list.SelectedItem ?? -1;
        if (i < 0 || i >= _displayRows.Count) return;
        var row = _displayRows[i];
        if (row.Binding is null) return;
        _currentBindings.RemoveAll(b =>
            string.Equals(b.CommandId, row.CommandId, StringComparison.Ordinal)
            && string.Equals(b.Sequence, row.Binding, StringComparison.Ordinal));
        RebuildRows();
    }

    private sealed record Row(string CommandId, string Label, string? Binding);
}
