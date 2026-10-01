using System.Collections;
using System.Collections.Specialized;
using TuiCode.Abstractions;
using TuiCode.Workbench.Services;

namespace TuiCode.Workbench.Git;

/// <summary>Modal picker for Open worktree (<c>ow</c>): a filter over the repo's other worktrees.</summary>
public sealed class WorktreePickerView : Window
{
    private const string Hint = "Type to filter · Up/Down/PgUp/PgDn · Enter open · Esc cancel";

    private readonly TextField _filter;
    private readonly Label _header;
    private readonly ListView _list;

    private readonly ICommandService _scopeCommands;
    private readonly IKeybindingService _scopeKeybindings;

    private readonly IReadOnlyList<WorktreeRow> _all;
    private readonly int _branchWidth;
    private readonly int _nameWidth;
    private IReadOnlyList<WorktreeRow> _visible = [];

    public IKeybindingService Scope => _scopeKeybindings;

    public event EventHandler? Cancelled;

    public event EventHandler<WorktreeRow>? Submitted;

    public WorktreePickerView(IReadOnlyList<WorktreeRow> worktrees)
    {
        _all = worktrees;
        _branchWidth = worktrees.Append(WorktreeList.Headings).Max(w => w.Branch.Length);
        _nameWidth = worktrees.Append(WorktreeList.Headings).Max(w => w.Name.Length);
        Title = "Open worktree";
        BorderStyle = LineStyle.Single;
        X = Pos.Center();
        Y = Pos.Center();
        Width = 78;
        Height = 18;
        CanFocus = true;

        _filter = new TextField { X = 1, Y = 0, Width = Dim.Fill(1) };
        _filter.TextChanged += (_, _) => ShowEntries();

        _header = new Label { X = 1, Y = Pos.Bottom(_filter) + 1, Width = Dim.Fill(1), Height = 1 };
        _header.GettingAttributeForRole += (_, e) =>
        {
            var attribute = e.Result ?? GetAttributeForRole(e.Role);
            e.Result = attribute with { Foreground = attribute.Background, Background = attribute.Foreground };
            e.Handled = true;
        };

        _list = new ListView { X = 1, Y = Pos.Bottom(_header), Width = Dim.Fill(1), Height = Dim.Fill(1) };
        var hint = new Label { X = 1, Y = Pos.AnchorEnd(1), Width = Dim.Fill(1), Text = Hint };
        Add(_filter, _header, _list, hint);

        _scopeCommands = new CommandService();
        _scopeKeybindings = new KeybindingService(_scopeCommands);
        RegisterScopeBindings();
        ShowEntries();
    }

    internal string Filter => _filter.Text ?? "";
    internal IReadOnlyList<string> VisibleItems => [.. _visible.Select(w => w.Name)];
    internal int? SelectedItem => _list.SelectedItem;
    internal string Header => _header.Text;

    public bool FocusFilter() => _filter.SetFocus();

    protected override void OnSubViewsLaidOut(LayoutEventArgs args)
    {
        base.OnSubViewsLaidOut(args);
        var header = WorktreeList.Header(_branchWidth, _nameWidth, _list.Viewport.Width);
        if (_header.Text != header)
            _header.Text = header;
    }

    private void RegisterScopeBindings()
    {
        _scopeCommands.Register(CommandIds.WorktreePickerCancel, () => Cancelled?.Invoke(this, EventArgs.Empty));
        _scopeCommands.Register(CommandIds.WorktreePickerConfirm, OnConfirm);
        _scopeCommands.Register(CommandIds.WorktreePickerUp, () => MoveSelection(-1));
        _scopeCommands.Register(CommandIds.WorktreePickerDown, () => MoveSelection(1));
        _scopeCommands.Register(CommandIds.WorktreePickerPageUp, () => MoveSelection(-PageHeight));
        _scopeCommands.Register(CommandIds.WorktreePickerPageDown, () => MoveSelection(PageHeight));

        _scopeKeybindings.Bind("Esc", CommandIds.WorktreePickerCancel);
        _scopeKeybindings.Bind("Enter", CommandIds.WorktreePickerConfirm);
        _scopeKeybindings.Bind("CursorUp", CommandIds.WorktreePickerUp);
        _scopeKeybindings.Bind("CursorDown", CommandIds.WorktreePickerDown);
        _scopeKeybindings.Bind("PageUp", CommandIds.WorktreePickerPageUp);
        _scopeKeybindings.Bind("PageDown", CommandIds.WorktreePickerPageDown);
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
        _visible = WorktreeList.Filter(_all, Filter);
        _list.Source = new WorktreeListSource(_visible, _branchWidth, _nameWidth);
        _list.SelectedItem = _visible.Count == 0 ? null : 0;
    }

    private sealed class WorktreeListSource(IReadOnlyList<WorktreeRow> rows, int branchWidth, int nameWidth) : IListDataSource
    {
        public event NotifyCollectionChangedEventHandler? CollectionChanged { add { } remove { } }

        public int Count => rows.Count;

        public int MaxItemLength => 0;

        public bool SuspendCollectionChangedEvent { get; set; }

        public void Render(ListView listView, bool selected, int item, int col, int row, int width, int viewportX = 0)
        {
            listView.Move(col, row);
            listView.AddStr(WorktreeList.Display(rows[item], branchWidth, nameWidth, width).PadRight(width));
        }

        public bool IsMarked(int item) => false;

        public void SetMark(int item, bool value) { }

        public IList ToList() => rows.Select(r => r.Path).ToList();

        public void Dispose() { }
    }
}
