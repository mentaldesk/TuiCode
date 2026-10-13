using TuiCode.Abstractions;
using TuiCode.Workbench.Git;

namespace TuiCode.Workbench.History;

/// <summary>
/// Keeps the History panel (#534) on the active tab's file: it reloads when that file changes, and folds away
/// while the tab has no history. A history diff counts as its file, so stepping through one doesn't reload.
/// </summary>
internal sealed class FileHistoryPanel
{
    private readonly Workbench _workbench;
    private readonly IGitCli _git;
    private readonly Action<Action> _post;
    private int _load;
    private bool _listed;
    private bool _turningOn;

    public FileHistoryPanel(Workbench workbench, IGitCli git, Action<Action> post)
    {
        _workbench = workbench;
        _git = git;
        _post = post;
        workbench.Editor.Groups.ActiveTabChanged += (_, _) => Follow(announce: false);
        workbench.HistoryOnChanged += (_, _) => Follow(announce: _turningOn);
    }

    private HistoryView View => _workbench.History;

    /// <summary>The root of the repo the listed file is in.</summary>
    public string? RepoRoot { get; private set; }

    /// <summary>Why turning the panel on showed nothing, for the status bar.</summary>
    public event EventHandler<string>? NothingToShow;

    /// <summary>Turning the panel on listed the file, so the keys can go to it.</summary>
    public event EventHandler? ShownOnRequest;

    /// <summary>Completes once the loads started so far have been shown.</summary>
    internal Task Idle { get; private set; } = Task.CompletedTask;

    public void Toggle()
    {
        _turningOn = !_workbench.IsHistoryOn;
        try
        {
            _workbench.SetHistoryOn(_turningOn);
        }
        finally
        {
            _turningOn = false;
        }
    }

    private void Follow(bool announce)
    {
        if (!_workbench.IsHistoryOn)
        {
            _workbench.ShowHistory(false);
            return;
        }

        var group = _workbench.Editor.Group;
        var diff = group.ActiveDiffTab;
        if ((group.ActiveTab?.File ?? diff?.File) is not { } file)
        {
            Fold(announce ? "No file is open." : null);
            return;
        }
        if (!announce && file.FullName == View.FilePath)
        {
            if (_listed) _workbench.ShowHistory(true);
            if (diff?.History is { } spot && spot.Path == View.FilePath) View.Select(spot.Commit);
            return;
        }
        Load(file, announce);
    }

    private void Load(IFileInfo file, bool announce)
    {
        var load = ++_load;
        var (path, name) = (file.FullName, file.Name);
        _listed = false;
        View.ShowLoading(path, name);
        if (!file.FileSystem.File.Exists(path))
        {
            Fold(announce ? $"{name} has never been saved." : null);
            return;
        }
        if (!GitRepository.Contains(file.Directory))
        {
            Fold(announce ? $"{name} isn't in a git repository." : null);
            return;
        }

        var root = Task.Run(() => _git.GetRepoRootAsync(path));
        var first = Task.Run(() => _git.GetFileCommitsAsync(path, FileHistory.FirstPage));
        Idle = Task.WhenAll(root, first).ContinueWith(_ => _post(() =>
        {
            if (load != _load || !root.IsCompletedSuccessfully || !first.IsCompletedSuccessfully) return;
            if ((root.Result.Error ?? first.Result.Error) is { } error)
            {
                Fold(announce ? error : null);
                return;
            }
            if (root.Result.Value is not { } repoRoot || first.Result.Value is null)
            {
                Fold(announce ? $"{name} isn't in a git repository." : null);
                return;
            }
            if (first.Result.Value is not { Count: > 0 } commits)
            {
                Fold(announce ? $"{name} isn't tracked by git." : null);
                return;
            }

            RepoRoot = repoRoot;
            _listed = true;
            View.Show(commits, capped: false);
            _workbench.ShowHistory(true);
            if (announce) ShownOnRequest?.Invoke(this, EventArgs.Empty);
            if (commits.Count == FileHistory.FirstPage) LoadRest(load, path);
        }), TaskScheduler.Default);
    }

    private void LoadRest(int load, string path)
    {
        var all = Task.Run(() => _git.GetFileCommitsAsync(path, FileHistory.Limit + 1));
        Idle = all.ContinueWith(_ => _post(() =>
        {
            if (load != _load || !all.IsCompletedSuccessfully || all.Result.Value is not { } commits) return;
            View.Show([.. commits.Take(FileHistory.Limit)], capped: commits.Count > FileHistory.Limit);
        }), TaskScheduler.Default);
    }

    private void Fold(string? reason)
    {
        _listed = false;
        _workbench.ShowHistory(false);
        if (reason is not null) NothingToShow?.Invoke(this, reason);
    }
}
