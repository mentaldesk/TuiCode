using System.Collections;
using System.Collections.Specialized;
using TuiCode.Abstractions;
using TuiCode.Workbench.Services;

namespace TuiCode.Workbench.Workspace;

/// <summary>Modal picker for Open recent folder (<c>or</c>): a filter over the folders you've had open, or a path to open.</summary>
public sealed class RecentFolderPickerView : Window
{
    private const string Hint = "Type to filter · Up/Down/PgUp/PgDn · Enter open · Esc cancel";
    private const string PathHint = "Type to filter · Enter open this path · Esc cancel";

    private readonly TextField _filter;
    private readonly ListView _list;
    private readonly Label _status;
    private readonly Label _hint;

    private readonly ICommandService _scopeCommands;
    private readonly IKeybindingService _scopeKeybindings;

    private readonly IReadOnlyList<RecentFolder> _all;
    private readonly int _nameWidth;
    private readonly string _home;
    private IReadOnlyList<RecentFolder> _visible = [];
    private string? _typedPath;

    public IKeybindingService Scope => _scopeKeybindings;

    public event EventHandler? Cancelled;

    public event EventHandler<RecentFolder>? Submitted;

    /// <summary>Enter on a typed path, expanded; the host answers a missing folder with <see cref="ShowNoSuchFolder"/>.</summary>
    public event EventHandler<string>? PathSubmitted;

    public RecentFolderPickerView(IReadOnlyList<RecentFolder> folders, string home)
    {
        _all = folders;
        _home = home;
        _nameWidth = folders.Max(f => f.Name.Length);
        Title = "Open recent folder";
        BorderStyle = LineStyle.Single;
        X = Pos.Center();
        Y = Pos.Center();
        Width = 78;
        Height = 16;
        CanFocus = true;

        _filter = new TextField { X = 1, Y = 0, Width = Dim.Fill(1) };
        _filter.TextChanged += (_, _) => ShowEntries();

        _list = new ListView { X = 1, Y = 1, Width = Dim.Fill(1), Height = Dim.Fill(2) };
        _status = new Label { X = 1, Y = Pos.AnchorEnd(2), Width = Dim.Fill(1), Text = "" };
        _hint = new Label { X = 1, Y = Pos.AnchorEnd(1), Width = Dim.Fill(1), Text = Hint };
        Add(_filter, _list, _status, _hint);

        _scopeCommands = new CommandService();
        _scopeKeybindings = new KeybindingService(_scopeCommands);
        RegisterScopeBindings();
        ShowEntries();
    }

    internal string Filter => _filter.Text ?? "";
    internal IReadOnlyList<string> VisibleItems => _typedPath is { } path ? [OpenRow(path)] : [.. _visible.Select(f => f.Name)];
    internal string Status => _status.Text ?? "";
    internal string HintText => _hint.Text ?? "";
    internal int? SelectedItem => _list.SelectedItem;

    public bool FocusFilter() => _filter.SetFocus();

    public void ShowNoSuchFolder() => _status.Text = $"No such folder: {_typedPath}";

    private void RegisterScopeBindings()
    {
        _scopeCommands.Register(CommandIds.RecentFolderPickerCancel, OnCancel);
        _scopeCommands.Register(CommandIds.RecentFolderPickerConfirm, OnConfirm);
        _scopeCommands.Register(CommandIds.RecentFolderPickerUp, () => MoveSelection(-1));
        _scopeCommands.Register(CommandIds.RecentFolderPickerDown, () => MoveSelection(1));
        _scopeCommands.Register(CommandIds.RecentFolderPickerPageUp, () => MoveSelection(-PageHeight));
        _scopeCommands.Register(CommandIds.RecentFolderPickerPageDown, () => MoveSelection(PageHeight));

        _scopeKeybindings.Bind("Esc", CommandIds.RecentFolderPickerCancel);
        _scopeKeybindings.Bind("Enter", CommandIds.RecentFolderPickerConfirm);
        _scopeKeybindings.Bind("CursorUp", CommandIds.RecentFolderPickerUp);
        _scopeKeybindings.Bind("CursorDown", CommandIds.RecentFolderPickerDown);
        _scopeKeybindings.Bind("PageUp", CommandIds.RecentFolderPickerPageUp);
        _scopeKeybindings.Bind("PageDown", CommandIds.RecentFolderPickerPageDown);
    }

    private void OnCancel() => Cancelled?.Invoke(this, EventArgs.Empty);

    private void OnConfirm()
    {
        if (_typedPath is { } path)
        {
            PathSubmitted?.Invoke(this, RecentFolderList.ExpandPath(path, _home));
            return;
        }
        if (_list.SelectedItem is { } i && i < _visible.Count) Submitted?.Invoke(this, _visible[i]);
    }

    private int PageHeight => Math.Max(1, _list.Viewport.Height);

    private void MoveSelection(int delta)
    {
        if (_typedPath is not null || _visible.Count == 0) return;
        _list.SelectedItem = Math.Clamp((_list.SelectedItem ?? -1) + delta, 0, _visible.Count - 1);
    }

    private void ShowEntries()
    {
        _status.Text = "";
        if (RecentFolderList.IsPath(Filter))
        {
            _typedPath = Filter.Trim();
            _visible = [];
            _list.Source = new ListWrapper<string>(new([OpenRow(_typedPath)]));
            _list.SelectedItem = 0;
            _hint.Text = PathHint;
            return;
        }

        _typedPath = null;
        _visible = RecentFolderList.Filter(_all, Filter);
        _list.Source = new RecentFolderListSource(_visible, _nameWidth);
        _list.SelectedItem = _visible.Count == 0 ? null : 0;
        _hint.Text = Hint;
    }

    private static string OpenRow(string path) => $"Open {path}";

    private sealed class RecentFolderListSource(IReadOnlyList<RecentFolder> rows, int nameWidth) : IListDataSource
    {
        public event NotifyCollectionChangedEventHandler? CollectionChanged { add { } remove { } }

        public int Count => rows.Count;

        public int MaxItemLength => 0;

        public bool SuspendCollectionChangedEvent { get; set; }

        public void Render(ListView listView, bool selected, int item, int col, int row, int width, int viewportX = 0)
        {
            listView.Move(col, row);
            listView.AddStr(RecentFolderList.Display(rows[item], nameWidth, width).PadRight(width));
        }

        public bool IsMarked(int item) => false;

        public void SetMark(int item, bool value) { }

        public IList ToList() => rows.Select(r => r.Path).ToList();

        public void Dispose() { }
    }
}
