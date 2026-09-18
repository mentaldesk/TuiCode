using TuiCode.Abstractions;
using TuiCode.Icons;
using TuiCode.Workbench.Services;

namespace TuiCode.Workbench.Navigation;

/// <summary>
/// Modal file/folder browser (VS Code's Ctrl+O). Lists the current directory — a
/// leading <c>../</c>, then sub-directories, then files. Up/Down select, Enter
/// drills into a directory or opens a file in the editor, Esc cancels. Tab moves to
/// the "Open this folder" button which switches the workspace to the directory
/// currently being browsed (closing the old workspace).
/// Typing filters everything below the directory by <see cref="CamelHumps"/> (#109); Esc clears the filter first.
///
/// Owns its own <see cref="ICommandService"/> + <see cref="IKeybindingService"/>;
/// the <see cref="WorkbenchHost"/> pushes <see cref="Scope"/> on open and pops it on
/// <see cref="Cancelled"/> / a selection.
/// </summary>
public sealed class OpenView : Window
{
    private const string BrowseHint = "Type to filter · Enter open · Tab -> Open folder · Esc cancel";
    private const string FilterHint = "Up/Down select · Enter open · Esc clear filter";

    private readonly Label _pathLabel;
    private readonly Label _filterLabel;
    private readonly Label _hint;
    private readonly ListView _list;
    private readonly Button _openFolderButton;

    private readonly ICommandService _scopeCommands;
    private readonly IKeybindingService _scopeKeybindings;
    private readonly FileIcons? _icons;

    private IDirectoryInfo _currentDirectory;
    private List<OpenEntry> _entries = new();
    private string _filter = "";
    private QuickOpenIndex? _index;
    private CancellationTokenSource? _scanCts;

    public IKeybindingService Scope => _scopeKeybindings;

    public event EventHandler? Cancelled;
    public event EventHandler<IFileInfo>? FileSelected;
    public event EventHandler<IDirectoryInfo>? FolderSelected;

    public OpenView(IDirectoryInfo startDirectory, FileIcons? icons = null)
    {
        ArgumentNullException.ThrowIfNull(startDirectory);
        _currentDirectory = startDirectory;
        _icons = icons;

        Title = "Open File or Folder";
        BorderStyle = LineStyle.Single;
        X = Pos.Center();
        Y = Pos.Center();
        Width = 70;
        Height = 22;
        // Required for descendant focus — same reason as the other modals.
        CanFocus = true;

        _pathLabel = new Label { X = 1, Y = 0, Width = Dim.Fill(1) };
        _filterLabel = new Label { X = 1, Y = 1, Width = Dim.Fill(1) };

        _list = new ListView
        {
            X = 1,
            Y = 2,
            Width = Dim.Fill(1),
            Height = Dim.Fill(3),
        };
        _list.MouseEvent += (_, _) => _list.SetFocus();

        _openFolderButton = new Button
        {
            X = 1,
            Y = Pos.AnchorEnd(2),
            Text = "Open this folder",
        };
        _openFolderButton.Accepting += (_, _) => OpenCurrentFolder();

        _hint = new Label
        {
            X = 1,
            Y = Pos.AnchorEnd(1),
            Width = Dim.Fill(1),
            Text = BrowseHint,
        };

        Add(_pathLabel, _filterLabel, _list, _openFolderButton, _hint);

        _scopeCommands = new CommandService();
        _scopeKeybindings = new FilterScope(new KeybindingService(_scopeCommands), OnFilterKey);
        RegisterScopeBindings();

        Navigate(startDirectory);
    }

    public bool FocusList() => _list.SetFocus();

    private void RegisterScopeBindings()
    {
        _scopeCommands.Register(CommandIds.OpenCancel, OnCancel);
        _scopeCommands.Register(CommandIds.OpenConfirm, OnConfirm);

        _scopeKeybindings.Bind("Esc", CommandIds.OpenCancel);
        _scopeKeybindings.Bind("Enter", CommandIds.OpenConfirm);
    }

    internal string Filter => _filter;
    internal IReadOnlyList<string> VisibleItems => _entries.Select(e => e.Display).ToList();

    private void OnCancel()
    {
        if (_filter.Length > 0) SetFilter("");
        else Cancelled?.Invoke(this, EventArgs.Empty);
    }

    private bool OnFilterKey(Key key)
    {
        if (!key.IsCtrl && !key.IsAlt && key.TryGetPrintableRune(out var rune))
            SetFilter(_filter + rune);
        else if ((key == Key.Backspace || key == Key.Delete) && _filter.Length > 0)
            SetFilter(_filter[..^1]);
        else
            return false;
        return true;
    }

    internal void SetFilter(string filter)
    {
        _filter = filter;
        if (filter.Length > 0 && !_list.HasFocus) _list.SetFocus();
        ShowEntries();
    }

    private void Navigate(IDirectoryInfo directory)
    {
        _scanCts?.Cancel();
        _scanCts = null;
        _index = null;
        _currentDirectory = directory;
        _filter = "";
        _pathLabel.Text = Ellipsize(directory.FullName, 66);
        ShowEntries();
    }

    private void ShowEntries()
    {
        if (_filter.Length == 0)
        {
            _entries = DirectoryListing.Build(_currentDirectory);
            _filterLabel.Text = "";
            _hint.Text = BrowseHint;
        }
        else
        {
            if (_index is null) StartScan();
            _entries = _index?.Filter(_filter).Select(e => new OpenEntry(e.Kind, e.Display, e.Info)).ToList() ?? new();
            _filterLabel.Text = $"Filter: {_filter}" + (_index switch
            {
                null => "  (scanning…)",
                { Truncated: true } => $"  (first {_index.Entries.Count:N0} entries)",
                _ when _entries.Count == 0 => "  (no matches)",
                _ => "",
            });
            _hint.Text = FilterHint;
        }

        var items = _entries.Select(e => e.Display).ToList();
        _list.Source = _icons is { } icons
            ? new IconListSource(items, _entries.Select(e => e.Kind == OpenEntryKind.File ? icons.ForFile(e.Info.Name) : icons.ForDirectory(false)).ToList())
            : new ListWrapper<string>(new(items));
        _list.SelectedItem = _entries.Count > 0 ? 0 : null;
    }

    private void StartScan()
    {
        if (_scanCts is not null) return;
        var directory = _currentDirectory;
        if (App is not { } app)
        {
            _index = QuickOpenIndex.Scan(directory);
            return;
        }

        var cts = _scanCts = new CancellationTokenSource();
        Task.Run(() => QuickOpenIndex.Scan(directory, cancellationToken: cts.Token), cts.Token)
            .ContinueWith(t =>
            {
                if (!t.IsCompletedSuccessfully) return;
                app.Invoke(() =>
                {
                    if (!ReferenceEquals(_scanCts, cts)) return;
                    _index = t.Result;
                    if (_filter.Length > 0) ShowEntries();
                });
            }, TaskScheduler.Default);
    }

    // Enter is bound in the modal scope, so it never reaches the focused control.
    // Route it ourselves: a focused button opens the current folder; otherwise act
    // on the highlighted entry.
    private void OnConfirm()
    {
        if (_openFolderButton.HasFocus)
        {
            OpenCurrentFolder();
            return;
        }

        var i = _list.SelectedItem ?? -1;
        if (i < 0 || i >= _entries.Count) return;

        var entry = _entries[i];
        switch (entry.Kind)
        {
            case OpenEntryKind.Parent:
            case OpenEntryKind.Directory:
                Navigate((IDirectoryInfo)entry.Info);
                break;
            case OpenEntryKind.File:
                FileSelected?.Invoke(this, (IFileInfo)entry.Info);
                break;
        }
    }

    private void OpenCurrentFolder() => FolderSelected?.Invoke(this, _currentDirectory);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _scanCts?.Cancel();
            _scanCts = null;
        }
        base.Dispose(disposing);
    }

    // Keep the tail of a long path — the leaf directory is what the user cares about.
    private static string Ellipsize(string s, int max) =>
        s.Length <= max ? s : "…" + s[^(max - 1)..];

    private sealed class FilterScope(IKeybindingService inner, Func<Key, bool> onFilterKey) : IKeybindingService
    {
        public KeyHandlingResult Handle(Key key) =>
            onFilterKey(key) ? KeyHandlingResult.Consumed : inner.Handle(key);

        public void Bind(string keySequence, string commandId) => inner.Bind(keySequence, commandId);
        public void Bind(IReadOnlyList<Key> chord, string commandId) => inner.Bind(chord, commandId);
        public bool Unbind(string keySequence) => inner.Unbind(keySequence);
        public bool Unbind(IReadOnlyList<Key> chord) => inner.Unbind(chord);
        public void Reset() => inner.Reset();
        public KeybindingConflict? CheckConflict(string keySequence) => inner.CheckConflict(keySequence);
        public KeybindingConflict? CheckConflict(IReadOnlyList<Key> chord) => inner.CheckConflict(chord);
        public IEnumerable<KeyBinding> Bindings => inner.Bindings;
        public string? CurrentChord => inner.CurrentChord;

        public event EventHandler<string?>? ChordChanged
        {
            add => inner.ChordChanged += value;
            remove => inner.ChordChanged -= value;
        }
    }
}

internal enum OpenEntryKind { Parent, Directory, File }

internal sealed record OpenEntry(OpenEntryKind Kind, string Display, IFileSystemInfo Info);

internal static class DirectoryListing
{
    /// <summary>
    /// Build the browse list for a directory: a leading <c>../</c> (when there's a parent),
    /// then sub-directories and files, each alphabetically. Pure — no TG dependency, so it's
    /// unit-tested directly against a <c>MockFileSystem</c>.
    /// </summary>
    public static List<OpenEntry> Build(IDirectoryInfo directory)
    {
        var entries = new List<OpenEntry>();
        if (directory.Parent is { } parent)
            entries.Add(new OpenEntry(OpenEntryKind.Parent, "../", parent));

        // Enumeration can throw on an unreadable directory (permissions, a delete mid-browse);
        // degrade to whatever we managed to list rather than tearing down the modal.
        try
        {
            foreach (var d in directory.EnumerateDirectories().OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase))
                entries.Add(new OpenEntry(OpenEntryKind.Directory, d.Name + "/", d));
            foreach (var f in directory.EnumerateFiles().OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
                entries.Add(new OpenEntry(OpenEntryKind.File, f.Name, f));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        return entries;
    }
}
