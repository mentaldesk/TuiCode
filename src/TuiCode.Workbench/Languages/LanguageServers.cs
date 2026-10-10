using TuiCode.Editor;

namespace TuiCode.Workbench.Languages;

/// <summary>One language server per language for the open folder, kept in step with the open tabs.</summary>
public sealed class LanguageServers : IDisposable
{
    public static readonly IReadOnlyList<LanguageServerSpec> Known = [LanguageServerSpec.CSharp];

    /// <summary>How long typing has to pause before the server hears about it.</summary>
    public static readonly TimeSpan ChangeDelay = TimeSpan.FromMilliseconds(250);

    private readonly EditorGroups _group;
    private readonly ILanguageServerLauncher _launcher;
    private readonly Action<Action> _post;
    private readonly Action<TimeSpan, Action>? _schedule;
    private readonly Dictionary<string, LanguageServer> _servers = new(StringComparer.Ordinal);
    private readonly Dictionary<EditorTab, Synced> _tabs = new();
    private readonly HashSet<EditorTab> _changed = new();
    private string? _root;
    private bool _flushScheduled;

    /// <param name="post">Runs an action on the UI thread.</param>
    /// <param name="schedule">Runs an action on the UI thread after a delay; null sends every edit at once.</param>
    public LanguageServers(EditorGroups group, ILanguageServerLauncher launcher, Action<Action> post, Action<TimeSpan, Action>? schedule = null)
    {
        _group = group;
        _launcher = launcher;
        _post = post;
        _schedule = schedule;
        _group.TabsChanged += OnTabsChanged;
        _group.GrammarChanged += OnGrammarChanged;
    }

    /// <summary>A server started, loaded, stopped or turned out not to be installed. Raised on the UI thread.</summary>
    public event EventHandler? StateChanged;

    /// <summary>Applied to every server started from now on.</summary>
    public TimeSpan QuietLoad { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>Applied to every server started from now on.</summary>
    public TimeSpan StopTimeout { get; init; } = TimeSpan.FromSeconds(1);

    public IReadOnlyCollection<LanguageServer> Running => _servers.Values;

    /// <summary>The server <paramref name="tab"/>'s language would use, whether or not it has started.</summary>
    public static LanguageServerSpec? SpecFor(EditorTab tab) =>
        tab.HasSyntax ? Known.FirstOrDefault(spec => spec.LanguageId == tab.Grammar?.Id) : null;

    public LanguageServer? ServerFor(EditorTab tab) =>
        SpecFor(tab) is { } spec ? _servers.GetValueOrDefault(spec.LanguageId) : null;

    /// <summary>Stops every server; the next file opened starts its language's server in <paramref name="root"/>.</summary>
    public void OpenFolder(string root)
    {
        foreach (var tab in _tabs.Keys.ToList()) Untrack(tab);
        _changed.Clear();
        StopAll(wait: false);
        _root = root;
    }

    /// <summary>Sends the edits still waiting out <see cref="ChangeDelay"/>, so a request sees the buffer as it is.</summary>
    public void Flush()
    {
        foreach (var tab in _changed)
            if (_tabs.TryGetValue(tab, out var synced)) synced.Server.Change(synced.Path, tab.Content);
        _changed.Clear();
    }

    private void OnTabsChanged(object? sender, EventArgs e) => Reconcile();

    private void OnGrammarChanged(object? sender, EditorTab tab) => Reconcile();

    private void Reconcile()
    {
        var open = _group.Tabs;
        foreach (var tab in _tabs.Keys.Except(open).ToList()) Untrack(tab);
        foreach (var tab in open)
        {
            var spec = SpecFor(tab);
            if (_tabs.TryGetValue(tab, out var synced))
            {
                if (synced.Path == tab.File.FullName && synced.Server.Spec == spec) continue;
                Untrack(tab);
            }
            if (spec is not null) Track(tab, Start(spec, tab));
        }
    }

    private LanguageServer Start(LanguageServerSpec spec, EditorTab tab)
    {
        if (_servers.TryGetValue(spec.LanguageId, out var running)) return running;
        var root = _root ?? tab.File.DirectoryName ?? tab.File.FullName;
        var server = new LanguageServer(spec, root, _launcher, _post) { QuietLoad = QuietLoad, StopTimeout = StopTimeout };
        server.StateChanged += (_, _) => StateChanged?.Invoke(this, EventArgs.Empty);
        _servers[spec.LanguageId] = server;
        server.Start();
        return server;
    }

    private void Track(EditorTab tab, LanguageServer server)
    {
        var synced = new Synced(tab.File.FullName, server);
        synced.Edited = (_, _) => OnEdited(tab);
        synced.Saved = (_, _) =>
        {
            _changed.Remove(tab);
            server.Save(synced.Path, tab.Content);
        };
        tab.ContentChanged += synced.Edited;
        tab.Saved += synced.Saved;
        _tabs[tab] = synced;
        server.Open(synced.Path, tab.Content);
    }

    private void Untrack(EditorTab tab)
    {
        if (!_tabs.Remove(tab, out var synced)) return;
        tab.ContentChanged -= synced.Edited;
        tab.Saved -= synced.Saved;
        _changed.Remove(tab);
        synced.Server.Close(synced.Path);
    }

    private void OnEdited(EditorTab tab)
    {
        _changed.Add(tab);
        if (_schedule is null)
        {
            Flush();
            return;
        }
        if (_flushScheduled) return;
        _flushScheduled = true;
        _schedule(ChangeDelay, () =>
        {
            _flushScheduled = false;
            Flush();
        });
    }

    private void StopAll(bool wait)
    {
        var stopping = _servers.Values.Select(server => server.StopAsync()).ToArray();
        _servers.Clear();
        if (wait) Task.WaitAll(stopping);
    }

    public void Dispose()
    {
        _group.TabsChanged -= OnTabsChanged;
        _group.GrammarChanged -= OnGrammarChanged;
        foreach (var tab in _tabs.Keys.ToList()) Untrack(tab);
        StopAll(wait: true);
    }

    private sealed class Synced(string path, LanguageServer server)
    {
        public string Path { get; } = path;
        public LanguageServer Server { get; } = server;
        public EventHandler? Edited { get; set; }
        public EventHandler? Saved { get; set; }
    }
}
