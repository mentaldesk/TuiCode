using System.Collections;
using System.Collections.Specialized;
using TuiCode.Abstractions;
using TuiCode.Workbench.Services;

namespace TuiCode.Workbench.Git;

/// <summary>
/// Modal picker for Open pull request (<c>opr</c>): a filter over the repo's open PRs. Enter checks the
/// highlighted one out in its own worktree, which the host reports back through <see cref="ShowError"/>
/// when it can't. The list is handed in already loaded, so there's no waiting state but the checkout's.
/// </summary>
public sealed class PullRequestPickerView : Window
{
    private const string Hint = "Type to filter · Up/Down/PgUp/PgDn · Enter check out · Esc cancel";

    private readonly TextField _filter;
    private readonly ListView _list;
    private readonly Label _status;

    private readonly ICommandService _scopeCommands;
    private readonly IKeybindingService _scopeKeybindings;

    private readonly IReadOnlyList<GitHubPullRequestSummary> _all;
    private IReadOnlyList<GitHubPullRequestSummary> _visible = [];

    public IKeybindingService Scope => _scopeKeybindings;

    public event EventHandler? Cancelled;

    /// <summary>The PR to check out; the host does the work and closes the picker once it's done.</summary>
    public event EventHandler<GitHubPullRequestSummary>? Submitted;

    public PullRequestPickerView(IReadOnlyList<GitHubPullRequestSummary> pullRequests)
    {
        _all = pullRequests;
        Title = "Open pull request";
        BorderStyle = LineStyle.Single;
        X = Pos.Center();
        Y = Pos.Center();
        Width = 78;
        Height = 22;
        CanFocus = true;

        _filter = new TextField { X = 1, Y = 0, Width = Dim.Fill(1) };
        _filter.TextChanged += (_, _) => ShowEntries();

        _list = new ListView { X = 1, Y = 1, Width = Dim.Fill(1), Height = Dim.Fill(3) };
        _status = new Label { X = 1, Y = Pos.AnchorEnd(3), Width = Dim.Fill(1), Text = "" };
        var legend = new Label { X = 1, Y = Pos.AnchorEnd(2), Width = Dim.Fill(1), Text = $"{PullRequestList.RequestedMarker} your review is requested" };
        var hint = new Label { X = 1, Y = Pos.AnchorEnd(1), Width = Dim.Fill(1), Text = Hint };
        Add(_filter, _list, _status, legend, hint);

        _scopeCommands = new CommandService();
        _scopeKeybindings = new KeybindingService(_scopeCommands);
        RegisterScopeBindings();
        ShowEntries();
    }

    internal string Filter => _filter.Text ?? "";
    internal string Status => _status.Text ?? "";
    internal IReadOnlyList<string> VisibleItems => [.. _visible.Select(pr => $"#{pr.Number}")];
    internal int? SelectedItem => _list.SelectedItem;

    public bool FocusFilter() => _filter.SetFocus();

    /// <summary>Shows why the checkout didn't happen; the picker stays open so another PR can be picked.</summary>
    public void ShowError(string message) => _status.Text = message;

    /// <summary>Shows what the host is doing while it checks a PR out.</summary>
    public void ShowBusy(int number) => _status.Text = $"Checking out #{number}…";

    private void RegisterScopeBindings()
    {
        _scopeCommands.Register(CommandIds.PullRequestPickerCancel, OnCancel);
        _scopeCommands.Register(CommandIds.PullRequestPickerConfirm, OnConfirm);
        _scopeCommands.Register(CommandIds.PullRequestPickerUp, () => MoveSelection(-1));
        _scopeCommands.Register(CommandIds.PullRequestPickerDown, () => MoveSelection(1));
        _scopeCommands.Register(CommandIds.PullRequestPickerPageUp, () => MoveSelection(-PageHeight));
        _scopeCommands.Register(CommandIds.PullRequestPickerPageDown, () => MoveSelection(PageHeight));

        _scopeKeybindings.Bind("Esc", CommandIds.PullRequestPickerCancel);
        _scopeKeybindings.Bind("Enter", CommandIds.PullRequestPickerConfirm);
        _scopeKeybindings.Bind("CursorUp", CommandIds.PullRequestPickerUp);
        _scopeKeybindings.Bind("CursorDown", CommandIds.PullRequestPickerDown);
        _scopeKeybindings.Bind("PageUp", CommandIds.PullRequestPickerPageUp);
        _scopeKeybindings.Bind("PageDown", CommandIds.PullRequestPickerPageDown);
    }

    private void OnCancel()
    {
        if (Filter.Length > 0) _filter.Text = "";
        else Cancelled?.Invoke(this, EventArgs.Empty);
    }

    private void OnConfirm()
    {
        if (_list.SelectedItem is { } i && i < _visible.Count) Submitted?.Invoke(this, _visible[i]);
    }

    private int PageHeight => Math.Max(1, _list.Viewport.Height);

    private void MoveSelection(int delta)
    {
        if (_visible.Count == 0) return;
        _list.SelectedItem = Math.Clamp((_list.SelectedItem ?? -1) + delta, 0, _visible.Count - 1);
    }

    private void ShowEntries()
    {
        _status.Text = "";
        _visible = PullRequestList.Filter(_all, Filter);
        _list.Source = new PullRequestListSource(_visible);
        _list.SelectedItem = _visible.Count == 0 ? null : 0;
    }

    private sealed class PullRequestListSource(IReadOnlyList<GitHubPullRequestSummary> rows) : IListDataSource
    {
        public event NotifyCollectionChangedEventHandler? CollectionChanged { add { } remove { } }

        public int Count => rows.Count;

        public int MaxItemLength => 0;

        public bool SuspendCollectionChangedEvent { get; set; }

        public void Render(ListView listView, bool selected, int item, int col, int row, int width, int viewportX = 0)
        {
            listView.Move(col, row);
            listView.AddStr(PullRequestList.Display(rows[item], width).PadRight(width));
        }

        public bool IsMarked(int item) => false;

        public void SetMark(int item, bool value) { }

        public IList ToList() => rows.Select(r => r.Title).ToList();

        public void Dispose() { }
    }
}
