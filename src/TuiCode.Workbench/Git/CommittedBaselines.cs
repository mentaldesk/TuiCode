using TuiCode.Abstractions;
using TuiCode.Editor;

namespace TuiCode.Workbench.Git;

/// <summary>
/// Gives each tab its file at <c>HEAD</c> to mark changes against, so a save doesn't clear what isn't committed
/// (#370). Read in the background on open, save and reload; wherever git has no answer the tab keeps its last save.
/// </summary>
internal sealed class CommittedBaselines
{
    private readonly EditorGroup _group;
    private readonly IGitCli _git;
    private readonly Action<Action> _post;
    private readonly Dictionary<EditorTab, (string Path, int Read)> _reads = [];
    private readonly List<Task> _pending = [];

    public CommittedBaselines(EditorGroup group, IGitCli git, Action<Action> post)
    {
        _group = group;
        _git = git;
        _post = post;
        _group.TabsChanged += (_, _) => Follow();
        _group.BaselineReset += (_, tab) => Read(tab);
        Follow();
    }

    /// <summary>Completes once every read started so far has been handed to its tab.</summary>
    internal Task Idle
    {
        get
        {
            lock (_pending) return Task.WhenAll(_pending.ToArray());
        }
    }

    private void Follow()
    {
        var open = _group.Tabs;
        foreach (var closed in _reads.Keys.Except(open).ToList())
            _reads.Remove(closed);
        foreach (var tab in open)
            if (!_reads.TryGetValue(tab, out var read) || read.Path != tab.File.FullName)
                Read(tab);
    }

    private void Read(EditorTab tab)
    {
        var path = tab.File.FullName;
        var read = _reads.TryGetValue(tab, out var last) ? last.Read + 1 : 0;
        _reads[tab] = (path, read);
        if (!GitRepository.Contains(tab.File.Directory))
        {
            if (tab.CommittedLines is not null) tab.CommittedLines = null;
            return;
        }

        var reading = Task.Run(async () =>
                await _git.ShowCheckedOutFileAsync(path) is { Succeeded: true, Value: { } text } ? DiffTab.SplitLines(text) : null)
            .ContinueWith(t => _post(() =>
            {
                // A read that returns after a newer one started, or after the tab closed, is stale.
                if (_reads.TryGetValue(tab, out var current) && current == (path, read))
                    tab.CommittedLines = t.IsCompletedSuccessfully ? t.Result : null;
            }), TaskScheduler.Default);
        lock (_pending)
        {
            _pending.RemoveAll(t => t.IsCompleted);
            _pending.Add(reading);
        }
    }
}
