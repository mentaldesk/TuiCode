using System.Collections;
using System.Collections.Specialized;
using MentalDesk.Tui.Loading;
using TuiCode.Abstractions;
using TuiCode.Workbench.Controls;
using TuiCode.Workbench.Services;

namespace TuiCode.Workbench.Git;

/// <summary>
/// Modal picker for Compare to revision (<c>ctr</c>): a filter over the file's branches, tags and
/// recent commits. Enter picks the highlighted row, or the typed text when nothing matches.
/// The list holds only <c>HEAD</c> until <see cref="Load"/>, so the host can fill it in off the UI thread;
/// Enter still takes <c>HEAD</c> meanwhile.
/// </summary>
public sealed class RevisionPickerView : Window
{
    private const string Hint = "Type to filter · Up/Down/PgUp/PgDn · Enter compare · Esc cancel";

    private readonly TextField _filter;
    private readonly ListView _list;
    private readonly LoadStateView _state;
    private readonly Label _error;

    private readonly ICommandService _scopeCommands;
    private readonly IKeybindingService _scopeKeybindings;

    private IReadOnlyList<RevisionEntry> _entries = RevisionList.Build([], []);
    private IReadOnlyList<RevisionEntry> _visible = [];
    private Action? _retry;

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

        _list = new ListView { Width = Dim.Fill(), Height = Dim.Fill() };
        _state = new LoadStateView(_list, "Loading branches, tags and commits…") { X = 1, Y = 1, Width = Dim.Fill(1), Height = Dim.Fill(3) };
        _error = new Label { X = 1, Y = Pos.AnchorEnd(2), Width = Dim.Fill(1), Text = "" };
        var hint = new Label { X = 1, Y = Pos.AnchorEnd(1), Width = Dim.Fill(1), Text = Hint };
        Add(_filter, _state, _error, hint);

        _scopeCommands = new CommandService();
        _scopeKeybindings = new KeybindingService(_scopeCommands);
        RegisterScopeBindings();
        ShowEntries();
    }

    internal string Filter => _filter.Text ?? "";
    internal string Error => _error.Text ?? "";
    internal IReadOnlyList<string> VisibleItems => _visible.Select(e => e.Text).ToList();
    internal int? SelectedItem => _list.SelectedItem;
    internal bool Loaded => _state.State == LoadState.Loaded;
    internal LoadStateView State => _state;

    public bool FocusFilter() => _filter.SetFocus();

    public void ShowLoading()
    {
        _retry = null;
        _state.ShowLoading();
    }

    public void Load(IReadOnlyList<GitRef> refs, IReadOnlyList<GitCommit> commits)
    {
        _entries = RevisionList.Build(refs, commits);
        _state.ShowContent();
        ShowEntries();
    }

    public void ShowLoadFailed(string message, Action? retry)
    {
        _retry = retry;
        _state.ShowLoadFailed(message, retry);
    }

    public void ShowError(string message) => _error.Text = message;

    private void RegisterScopeBindings()
    {
        _scopeCommands.Register(CommandIds.RevisionPickerCancel, OnCancel);
        _scopeCommands.Register(CommandIds.RevisionPickerConfirm, OnConfirm);
        _scopeCommands.Register(CommandIds.RevisionPickerUp, () => MoveSelection(-1));
        _scopeCommands.Register(CommandIds.RevisionPickerDown, () => MoveSelection(1));
        _scopeCommands.Register(CommandIds.RevisionPickerPageUp, () => MoveSelection(-PageHeight));
        _scopeCommands.Register(CommandIds.RevisionPickerPageDown, () => MoveSelection(PageHeight));
        _scopeCommands.Register(CommandIds.RevisionPickerRetry, "Retry", () => _retry?.Invoke(), CommandScope.Global, CanRetry);

        _scopeKeybindings.Bind("Esc", CommandIds.RevisionPickerCancel);
        _scopeKeybindings.Bind("Enter", CommandIds.RevisionPickerConfirm);
        _scopeKeybindings.Bind("CursorUp", CommandIds.RevisionPickerUp);
        _scopeKeybindings.Bind("CursorDown", CommandIds.RevisionPickerDown);
        _scopeKeybindings.Bind("PageUp", CommandIds.RevisionPickerPageUp);
        _scopeKeybindings.Bind("PageDown", CommandIds.RevisionPickerPageDown);
        _scopeKeybindings.Bind("r", CommandIds.RevisionPickerRetry);
    }

    private bool CanRetry() => _retry is not null && _state.State == LoadState.Failed;

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

    private int PageHeight => Math.Max(1, _list.Viewport.Height);

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
        _list.SelectedItem = RevisionList.Selected(_visible, Filter);
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
