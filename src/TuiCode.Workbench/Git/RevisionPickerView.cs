using System.Collections;
using System.Collections.Specialized;
using TuiCode.Abstractions;
using TuiCode.Workbench.Services;

namespace TuiCode.Workbench.Git;

/// <summary>
/// Modal picker for Compare to revision (<c>ctr</c>): a filter over the file's branches, tags and
/// recent commits. Enter picks the highlighted row, or the typed text when nothing matches.
/// The list is empty until <see cref="Load"/>, so the host can fill it in off the UI thread.
/// </summary>
public sealed class RevisionPickerView : Window
{
    private const string LoadingHint = "Loading branches, tags and commits…";
    private const string ReadyHint = "Type to filter · Up/Down select · Enter compare · Esc cancel";

    private readonly TextField _filter;
    private readonly ListView _list;
    private readonly Label _error;
    private readonly Label _hint;

    private readonly ICommandService _scopeCommands;
    private readonly IKeybindingService _scopeKeybindings;

    private IReadOnlyList<RevisionEntry> _entries = [];
    private IReadOnlyList<RevisionEntry> _visible = [];

    public IKeybindingService Scope => _scopeKeybindings;

    public event EventHandler? Cancelled;

    /// <summary>The revision to ask git for; the diff tab is titled with it.</summary>
    public event EventHandler<string>? Submitted;

    public RevisionPickerView(string fileName)
    {
        Title = $"Compare {fileName} to revision";
        BorderStyle = LineStyle.Single;
        X = Pos.Center();
        Y = Pos.Center();
        Width = 70;
        Height = 22;
        CanFocus = true;

        _filter = new TextField { X = 1, Y = 0, Width = Dim.Fill(1) };
        _filter.TextChanged += (_, _) => ShowEntries();

        _list = new ListView { X = 1, Y = 1, Width = Dim.Fill(1), Height = Dim.Fill(3) };
        _error = new Label { X = 1, Y = Pos.AnchorEnd(2), Width = Dim.Fill(1), Text = "" };
        _hint = new Label { X = 1, Y = Pos.AnchorEnd(1), Width = Dim.Fill(1), Text = LoadingHint };
        Add(_filter, _list, _error, _hint);

        _scopeCommands = new CommandService();
        _scopeKeybindings = new KeybindingService(_scopeCommands);
        RegisterScopeBindings();
        ShowEntries();
    }

    internal string Filter => _filter.Text ?? "";
    internal string Error => _error.Text ?? "";
    internal IReadOnlyList<string> VisibleItems => _visible.Select(e => e.Text).ToList();
    internal int? SelectedItem => _list.SelectedItem;

    public bool FocusFilter() => _filter.SetFocus();

    public void Load(IReadOnlyList<GitRef> refs, IReadOnlyList<GitCommit> commits)
    {
        _entries = RevisionList.Build(refs, commits);
        _hint.Text = ReadyHint;
        ShowEntries();
    }

    public void ShowError(string message) => _error.Text = message;

    private void RegisterScopeBindings()
    {
        _scopeCommands.Register(CommandIds.RevisionPickerCancel, OnCancel);
        _scopeCommands.Register(CommandIds.RevisionPickerConfirm, OnConfirm);
        _scopeCommands.Register(CommandIds.RevisionPickerUp, () => MoveSelection(-1));
        _scopeCommands.Register(CommandIds.RevisionPickerDown, () => MoveSelection(1));

        _scopeKeybindings.Bind("Esc", CommandIds.RevisionPickerCancel);
        _scopeKeybindings.Bind("Enter", CommandIds.RevisionPickerConfirm);
        _scopeKeybindings.Bind("CursorUp", CommandIds.RevisionPickerUp);
        _scopeKeybindings.Bind("CursorDown", CommandIds.RevisionPickerDown);
    }

    private void OnCancel()
    {
        if (Filter.Length > 0) _filter.Text = "";
        else Cancelled?.Invoke(this, EventArgs.Empty);
    }

    private void OnConfirm()
    {
        var revision = _list.SelectedItem is { } i && i < _visible.Count ? _visible[i].Revision : Filter.Trim();
        if (revision.Length > 0) Submitted?.Invoke(this, revision);
    }

    private void MoveSelection(int delta)
    {
        if (_visible.Count == 0) return;
        _list.SelectedItem = Math.Clamp((_list.SelectedItem ?? -1) + delta, 0, _visible.Count - 1);
    }

    private void ShowEntries()
    {
        _error.Text = "";
        _visible = RevisionList.Filter(_entries, Filter);
        _list.Source = new RevisionListSource(_visible);
        _list.SelectedItem = _visible.Count > 0 ? 0 : null;
    }

    private sealed class RevisionListSource(IReadOnlyList<RevisionEntry> rows) : IListDataSource
    {
        public event NotifyCollectionChangedEventHandler? CollectionChanged { add { } remove { } }

        public int Count => rows.Count;

        public int MaxItemLength => 0;

        public bool SuspendCollectionChangedEvent { get; set; }

        public void Render(ListView listView, bool selected, int item, int col, int row, int width, int viewportX = 0)
        {
            listView.Move(col, row);
            listView.AddStr(rows[item].Display(width).PadRight(width));
        }

        public bool IsMarked(int item) => false;

        public void SetMark(int item, bool value) { }

        public IList ToList() => rows.Select(r => r.Text).ToList();

        public void Dispose() { }
    }
}
