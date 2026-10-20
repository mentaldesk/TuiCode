using TuiCode.Editor;

namespace TuiCode.Workbench.Workspace;

/// <summary>
/// Keeps a copy of each dirty tab in <see cref="RecoveryStore"/> a moment after its last edit, and drops it once the tab
/// is clean or closed unsaved, so edits survive TuiCode ending without asking (#579). Copies are written in order, off
/// the UI thread; the buffers are only read on it.
/// </summary>
public sealed class UnsavedEdits : IDisposable
{
    private readonly EditorGroups _groups;
    private readonly RecoveryStore _store;
    private readonly Dictionary<EditorTab, Watched> _tabs = [];
    private readonly HashSet<string> _kept = new(StringComparer.Ordinal);
    private readonly Lock _writing = new();
    private Task _queue = Task.CompletedTask;
    private bool _stopped;

    public UnsavedEdits(EditorGroups groups, RecoveryStore store)
    {
        _groups = groups;
        _store = store;
        groups.Recovery = file => store.Find(file.FullName);
        groups.TabsChanged += OnTabsChanged;
        groups.EditorClosing += OnEditorClosing;
    }

    /// <summary>How long after the last edit a copy is kept.</summary>
    public TimeSpan Delay { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>Milliseconds on a clock that only goes forward.</summary>
    internal Func<long> Clock { get; set; } = () => Environment.TickCount64;

    /// <summary>Runs an action on the UI thread after a delay; nothing is kept until there's one.</summary>
    public Action<TimeSpan, Action>? Schedule { get; set; }

    /// <summary>Raised as a tab opens with edits recovered from a session that ended unexpectedly.</summary>
    public event EventHandler<EditorTab>? Recovered;

    /// <summary>Whether <paramref name="path"/> has edits to recover.</summary>
    public bool Has(string path) => _store.Find(path) is not null;

    /// <summary>Keeps every edit not yet kept now, before TuiCode exits on a hangup or terminate signal. Call on the UI thread.</summary>
    public void Flush()
    {
        if (_stopped) return;
        foreach (var (tab, watched) in _tabs.Where(t => t.Value.Due is not null).ToList())
        {
            watched.Due = null;
            Keep(tab, watched.Path);
        }
        Wait();
    }

    /// <summary>Drops the copies of <paramref name="tabs"/>, whose edits were thrown away on purpose.</summary>
    public void Discard(IEnumerable<EditorTab> tabs)
    {
        foreach (var tab in tabs)
            if (_tabs.TryGetValue(tab, out var watched)) Drop(watched);
    }

    /// <summary>Drops every copy and keeps no more: the user quit without saving.</summary>
    public void DiscardAll()
    {
        Discard(_tabs.Keys);
        _stopped = true;
        Wait();
    }

    /// <summary>Waits for the copies already on their way to disk.</summary>
    public void Wait()
    {
        Task queue;
        lock (_writing) queue = _queue;
        queue.Wait();
    }

    private void OnTabsChanged(object? sender, EventArgs e)
    {
        var open = _groups.Tabs;
        foreach (var gone in _tabs.Keys.Except(open).ToList())
        {
            _tabs[gone].Unwire();
            _tabs.Remove(gone);
        }
        foreach (var tab in open)
        {
            if (!_tabs.TryGetValue(tab, out var watched))
            {
                Watch(tab);
                continue;
            }
            if (watched.Path == tab.File.FullName) continue;
            // Renamed or moved: the copy follows the file.
            Drop(watched);
            watched.Path = tab.File.FullName;
            OnChanged(tab);
        }
    }

    private void Watch(EditorTab tab)
    {
        EventHandler changed = (_, _) => OnChanged(tab);
        tab.ContentChanged += changed;
        tab.DirtyChanged += changed;
        var watched = new Watched(tab.File.FullName, () =>
        {
            tab.ContentChanged -= changed;
            tab.DirtyChanged -= changed;
        });
        _tabs[tab] = watched;
        if (!tab.IsRecovered)
        {
            if (tab.IsDirty) OnChanged(tab);
            return;
        }

        // Keep ours before dropping theirs, so the edits are never only in memory.
        var path = watched.Path;
        if (tab.IsDirty) Keep(tab, path);
        Enqueue(() => _store.Forget(path));
        Recovered?.Invoke(this, tab);
    }

    private void OnEditorClosing(object? sender, EditorTab tab)
    {
        if (_tabs.TryGetValue(tab, out var watched)) Drop(watched);
    }

    private void OnChanged(EditorTab tab)
    {
        if (_stopped || !_tabs.TryGetValue(tab, out var watched)) return;
        if (!tab.IsDirty)
        {
            Drop(watched);
            return;
        }
        var scheduled = watched.Due is not null;
        watched.Due = Clock() + (long)Delay.TotalMilliseconds;
        if (!scheduled) Schedule?.Invoke(Delay, () => KeepWhenDue(tab));
    }

    private void KeepWhenDue(EditorTab tab)
    {
        if (_stopped || !_tabs.TryGetValue(tab, out var watched) || watched.Due is not { } due) return;
        var left = due - Clock();
        if (left > 0)
        {
            Schedule?.Invoke(TimeSpan.FromMilliseconds(left), () => KeepWhenDue(tab));
            return;
        }
        watched.Due = null;
        if (tab.IsDirty) Keep(tab, watched.Path);
    }

    private void Keep(EditorTab tab, string path)
    {
        var edits = new RecoveredEdits(tab.Content, tab.ViewPosition, tab.OnDisk);
        _kept.Add(path);
        Enqueue(() => _store.Write(path, edits));
    }

    private void Drop(Watched watched)
    {
        watched.Due = null;
        var path = watched.Path;
        if (!_kept.Remove(path)) return;
        Enqueue(() => _store.Delete(path));
    }

    private void Enqueue(Action write)
    {
        lock (_writing) _queue = _queue.ContinueWith(_ => write(), TaskScheduler.Default);
    }

    /// <summary>Keeps what's pending first: TuiCode is going without the user having quit, e.g. after a crash.</summary>
    public void Dispose()
    {
        try
        {
            Flush();
        }
        // A crash is being reported; failing to keep a copy mustn't stop that.
        catch (Exception)
        {
        }
        _groups.TabsChanged -= OnTabsChanged;
        _groups.EditorClosing -= OnEditorClosing;
        _groups.Recovery = null;
        foreach (var watched in _tabs.Values) watched.Unwire();
        _tabs.Clear();
        _stopped = true;
        Wait();
    }

    private sealed class Watched(string path, Action unwire)
    {
        public string Path { get; set; } = path;
        public long? Due { get; set; }
        public Action Unwire { get; } = unwire;
    }
}
