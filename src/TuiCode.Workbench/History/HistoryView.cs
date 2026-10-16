using System.Collections;
using System.Collections.Specialized;
using MentalDesk.Tui.Loading;
using Terminal.Gui.Text;
using TuiCode.Abstractions;
using TuiCode.Workbench.Controls;

namespace TuiCode.Workbench.History;

/// <summary>The panel's History tab (#534): the commits that changed one file, newest first, behind a filter.</summary>
public sealed class HistoryView : View
{
    private readonly TextField _filter;
    private readonly VerbatimLabel _note;
    private readonly ListView _list;
    private readonly LoadStateView _state;
    private IReadOnlyList<GitFileCommit> _commits = [];
    private IReadOnlyList<GitFileCommit> _visible = [];

    /// <summary><c>Enter</c> on a row.</summary>
    public event EventHandler<GitFileCommit>? CommitActivated;

    public HistoryView()
    {
        Title = "History";
        CanFocus = true;

        _filter = new TextField { X = 0, Y = 0, Width = Dim.Fill() };
        _filter.TextChanged += (_, _) => ShowCommits();
        _filter.KeyDown += OnFilterKey;
        _note = new VerbatimLabel { X = Pos.AnchorEnd(0), Y = 0, Width = 0, Text = "" };

        _list = new CommitList(OnListKey) { Width = Dim.Fill(), Height = Dim.Fill() };
        _list.Accepting += (_, e) =>
        {
            e.Handled = true;
            Activate();
        };
        _state = new LoadStateView(_list, "Loading history…") { X = 0, Y = 1, Width = Dim.Fill(), Height = Dim.Fill() };
        Add(_filter, _note, _state);
    }

    /// <summary>The full path of the file listed, or null before the first.</summary>
    public string? FilePath { get; private set; }

    /// <summary>Every commit listed, unfiltered.</summary>
    public IReadOnlyList<GitFileCommit> Commits => _commits;

    public GitFileCommit? SelectedCommit => _list.SelectedItem is { } i && i < _visible.Count ? _visible[i] : null;

    public bool ListHasFocus => _list.HasFocus;

    internal Func<DateTimeOffset> Now { get; set; } = () => DateTimeOffset.Now;
    internal string Filter => _filter.Text ?? "";
    internal string Note => _note.Text ?? "";
    internal LoadStateView State => _state;
    internal IReadOnlyList<GitFileCommit> Listed => _visible;
    internal IReadOnlyList<string> Rows => [.. Enumerable.Range(0, _visible.Count).Select(i => Row(i, _list.Viewport.Width))];

    /// <summary>Starts listing <paramref name="path"/>: empty and loading, the filter cleared.</summary>
    public void ShowLoading(string path, string name)
    {
        FilePath = path;
        Title = $"History: {name}";
        PaneTabs.ShowTitle(this);
        _commits = [];
        _filter.Text = "";
        SetNote("");
        ShowCommits();
        _state.ShowLoading();
    }

    /// <summary>Lists <paramref name="commits"/>, keeping the selected commit where it's still listed.</summary>
    public void Show(IReadOnlyList<GitFileCommit> commits, bool capped)
    {
        _commits = commits;
        SetNote(capped ? FileHistory.CappedNote : "");
        _state.ShowContent();
        ShowCommits();
    }

    public void ShowFailed(string message) => _state.ShowLoadFailed(message, null);

    /// <summary>Selects <paramref name="commit"/> when the filter shows it.</summary>
    public void Select(GitFileCommit commit)
    {
        var index = Index(_visible, commit.Hash);
        if (index >= 0) _list.SelectedItem = index;
    }

    /// <summary>The list, or the filter while nothing is listed.</summary>
    public bool FocusList() => _visible.Count == 0 ? _filter.SetFocus() : _list.SetFocus();

    private void SetNote(string note)
    {
        _note.Text = note;
        _note.Width = note.Length;
        _note.X = Pos.AnchorEnd(note.Length);
        _filter.Width = Dim.Fill(note.Length == 0 ? 0 : note.Length + 2);
    }

    private void ShowCommits()
    {
        var selected = SelectedCommit?.Hash;
        _visible = FileHistory.Filter(_commits, Filter);
        _list.Source = new Source(this);
        if (_visible.Count == 0) return;
        var index = selected is null ? 0 : Index(_visible, selected);
        _list.SelectedItem = Math.Max(0, index);
    }

    private static int Index(IReadOnlyList<GitFileCommit> commits, string hash)
    {
        for (var i = 0; i < commits.Count; i++)
            if (commits[i].Hash == hash) return i;
        return -1;
    }

    private void Activate()
    {
        if (SelectedCommit is { } commit) CommitActivated?.Invoke(this, commit);
    }

    private void OnFilterKey(object? sender, Key key)
    {
        int? delta = key == Key.CursorDown ? 1
            : key == Key.CursorUp ? -1
            : key == Key.PageDown ? PageHeight
            : key == Key.PageUp ? -PageHeight
            : null;
        if (delta is { } d)
        {
            Move(d);
            key.Handled = true;
        }
        else if (key == Key.Enter)
        {
            Activate();
            key.Handled = true;
        }
    }

    // Typing in the list goes to the filter, so the panel can open with the keys on the list.
    private bool OnListKey(Key key)
    {
        if (key == Key.Enter)
        {
            Activate();
            return true;
        }
        if (key.IsCtrl || key.IsAlt || !key.TryGetPrintableRune(out var rune)) return false;
        _filter.Text = Filter + rune;
        _filter.SetFocus();
        _filter.MoveEnd();
        return true;
    }

    private int PageHeight => Math.Max(1, _list.Viewport.Height);

    private void Move(int delta)
    {
        if (_visible.Count == 0) return;
        _list.SelectedItem = Math.Clamp((_list.SelectedItem ?? -1) + delta, 0, _visible.Count - 1);
    }

    private string Row(int item, int width) =>
        FileHistory.Row(_visible[item], FileHistory.Columns(_visible, Now()), Now(), width);

    // TG's ListView jumps to a row on a typed key in OnKeyDown, before KeyDown subscribers see it.
    private sealed class CommitList(Func<Key, bool> onKey) : ListView
    {
        protected override bool OnKeyDown(Key key) => onKey(key) || base.OnKeyDown(key);
    }

    private sealed class Source(HistoryView view) : IListDataSource
    {
        private readonly DateTimeOffset _now = view.Now();
        private readonly (int, int) _columns = FileHistory.Columns(view._visible, view.Now());

        public event NotifyCollectionChangedEventHandler? CollectionChanged { add { } remove { } }

        public int Count => view._visible.Count;

        public int MaxItemLength => 0;

        public bool SuspendCollectionChangedEvent { get; set; }

        public void Render(ListView listView, bool selected, int item, int col, int row, int width, int viewportX = 0)
        {
            listView.Move(col, row);
            var text = FileHistory.Row(view._visible[item], _columns, _now, width);
            listView.AddStr(text + new string(' ', Math.Max(0, width - text.GetColumns())));
        }

        public bool IsMarked(int item) => false;

        public void SetMark(int item, bool value) { }

        public IList ToList() => view._visible.Select(c => c.Subject).ToList();

        public void Dispose() { }
    }
}
