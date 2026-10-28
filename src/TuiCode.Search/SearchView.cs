using TuiCode.Icons;

namespace TuiCode.Search;

/// <summary>
/// Global find/replace panel (#33), hosted as the sidebar's Find tab next to the explorer. Typing re-runs the
/// search (in the background once the view is running, cancelling any search still in flight);
/// results show as a directory → file → match tree. Key handling for the inputs (Enter/Down to
/// the results, Tab between fields, Ctrl+Enter replace-all) is bound by the workbench against this
/// view's public methods.
/// </summary>
public sealed class SearchView : View
{
    private const int LabelWidth = 8;

    private readonly TextField _query;
    private readonly VerbatimLabel _replaceLabel;
    private readonly TextField _replacement;
    private readonly FindOption _matchCase;
    private readonly FindOption _wholeWord;
    private readonly FindOption _ignoredFiles;
    private readonly VerbatimLabel _status;
    private readonly TreeView<SearchNode> _results;
    private static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(250);

    private Running? _running;
    private (Request Request, string Replacement, int Matches)? _pendingReplace;
    // What Result was computed for — lags the inputs while a background search is in flight.
    private Request _resultFor = new(string.Empty, default, false);
    private bool _gitFailed;

    private readonly record struct Request(string Query, MatchOptions Options, bool IncludeIgnored);

    private sealed class Running
    {
        public readonly CancellationTokenSource Cts = new();
        public int FilesSearched;
    }

    public event EventHandler<(IFileInfo File, TextMatch Match)>? MatchActivated;

    /// <summary>Longer human-readable outcome (e.g. after replace-all), for the status bar.</summary>
    public event EventHandler<string>? Message;

    public Func<IDirectoryInfo?>? RootProvider { get; set; }
    public IOpenBuffers? OpenBuffers { get; set; }

    /// <summary>Lists the files git doesn't ignore; without it, or outside a repo, a fixed list of folders is skipped instead.</summary>
    public IGitCli? Git { get; set; }

    public Func<IDirectoryInfo, bool> InRepository { get; set; } = _ => false;

    public WorkspaceSearchResult Result { get; private set; } = WorkspaceSearchResult.Empty;
    public bool ReplaceVisible => _replacement.Visible;
    public bool InputsHaveFocus => _query.HasFocus || _replacement.HasFocus;
    public bool ReplacementHasFocus => _replacement.HasFocus;
    public string StatusText => _status.Text;
    public bool IsSearching => _running is not null;

    /// <summary>The key that stops a running search, shown while it runs; null shows none.</summary>
    public string? StopKey { get; set; }

    public MatchOptions Options => new(IsChecked(_matchCase), IsChecked(_wholeWord));
    public bool IncludeIgnored => IsChecked(_ignoredFiles);

    public bool IgnoredFilesAvailable => !_gitFailed && Git is not null && RootProvider?.Invoke() is { } root && InRepository(root);

    public string Query
    {
        get => _query.Text ?? string.Empty;
        set => _query.Text = value;
    }

    public string Replacement
    {
        get => _replacement.Text ?? string.Empty;
        set => _replacement.Text = value;
    }

    internal TreeView<SearchNode> Results => _results;

    public SearchView(FileIcons? icons = null)
    {
        CanFocus = true;

        var queryLabel = new VerbatimLabel { X = 0, Y = 0, Text = "Find" };
        _query = new TextField { X = LabelWidth, Y = 0, Width = Dim.Fill() };
        _replaceLabel = new VerbatimLabel { X = 0, Y = 1, Text = "Replace", Visible = false };
        _replacement = new TextField { X = LabelWidth, Y = 1, Width = Dim.Fill(), Visible = false };
        _matchCase = new FindOption("_Case") { X = LabelWidth, Y = 1 };
        _wholeWord = new FindOption("_Word") { X = Pos.Right(_matchCase) + 2, Y = 1 };
        _ignoredFiles = new FindOption("Ignored files") { X = LabelWidth, Y = 2 };
        _status = new VerbatimLabel { X = 0, Y = 3, Width = Dim.Fill(), Height = Dim.Auto(DimAutoStyle.Text), Text = string.Empty };
        _status.TextFormatter.WordWrap = true;
        _results = new TreeView<SearchNode>
        {
            X = 0,
            Y = Pos.Bottom(_status),
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            TreeBuilder = new DelegateTreeBuilder<SearchNode>(n => n.Children, n => n.Children.Count > 0),
        };

        Add(queryLabel, _query, _replaceLabel, _replacement, _matchCase, _wholeWord, _ignoredFiles, _status, _results);

        if (icons is not null)
        {
            _results.DrawLine += (_, e) =>
            {
                var icon = e.Model switch
                {
                    DirectoryNode => icons.ForDirectory(_results.IsExpanded(e.Model)),
                    FileNode file => icons.ForFile(file.Result.File.Name),
                    _ => null,
                };
                if (icon is { } i) IconDrawing.Prepend(e, i);
            };
            icons.Changed += (_, _) => _results.SetNeedsDraw();
        }

        _query.TextChanged += (_, _) => { _pendingReplace = null; RunSearch(); };
        _replacement.TextChanged += (_, _) => _pendingReplace = null;
        foreach (var option in new[] { _matchCase, _wholeWord, _ignoredFiles })
            option.ValueChanged += (_, _) => { _pendingReplace = null; RunSearch(); };
        _results.Activated += (_, _) => ActivateSelected();
        // Same TG quirk as the explorer: Enter maps to Command.Activate but doesn't raise Activated.
        _results.KeyDown += (_, key) =>
        {
            if (key != Key.Enter) return;
            ActivateSelected();
            key.Handled = true;
        };
    }

    public void ToggleMatchCase() => Toggle(_matchCase);
    public void ToggleWholeWord() => Toggle(_wholeWord);

    public void ToggleIgnoredFiles()
    {
        if (IgnoredFilesAvailable) Toggle(_ignoredFiles);
    }

    private static void Toggle(CheckBox option) =>
        option.Value = IsChecked(option) ? CheckState.UnChecked : CheckState.Checked;

    private static bool IsChecked(CheckBox option) => option.Value == CheckState.Checked;

    public bool FocusQuery()
    {
        var focused = _query.SetFocus();
        _query.SelectAll();
        return focused;
    }

    public bool FocusReplacement()
    {
        ShowReplace(true);
        var focused = _replacement.SetFocus();
        _replacement.SelectAll();
        return focused;
    }

    public bool FocusResults()
    {
        if (Result.Files.Count == 0) return false;
        _results.SelectedObject ??= FirstMatch();
        return _results.SetFocus();
    }

    /// <summary>Tab between the find and replace inputs (a no-op while replace is hidden).</summary>
    public void SwitchField()
    {
        if (!ReplaceVisible) return;
        if (_query.HasFocus) FocusReplacement();
        else FocusQuery();
    }

    public void ShowReplace(bool visible)
    {
        _replaceLabel.Visible = visible;
        _replacement.Visible = visible;
        var options = visible ? 2 : 1;
        _matchCase.Y = options;
        _wholeWord.Y = options;
        _ignoredFiles.Y = options + 1;
        _status.Y = options + 2;
        SetNeedsLayout();
    }

    /// <summary>Re-run the current query (e.g. after files changed on disk or a folder was opened).</summary>
    public void RunSearch()
    {
        _running?.Cts.Cancel();
        _running = null;

        var search = new Request(Query, Options, IncludeIgnored);
        var root = RootProvider?.Invoke();
        if (search.Query.Length == 0 || root is null)
        {
            Apply(root, search, WorkspaceSearchResult.Empty, root is null && search.Query.Length > 0 ? "No folder open" : string.Empty);
            return;
        }

        // Snapshot open buffers here, on the UI thread — TextView isn't safe to read from the worker.
        var buffers = OpenBuffers?.Snapshot();
        var git = InRepository(root) ? Git : null;
        if (App is not { } app)
        {
            Apply(root, search, Task.Run(() => FindAsync(root, search, buffers, git, new Running())).GetAwaiter().GetResult());
            return;
        }

        var run = _running = new Running();
        ShowProgress(run);
        app.AddTimeout(ProgressInterval, () =>
        {
            if (!ReferenceEquals(_running, run)) return false;
            ShowProgress(run);
            return true;
        });
        Task.Run(() => FindAsync(root, search, buffers, git, run), run.Cts.Token)
            .ContinueWith(t => app.Invoke(() =>
            {
                if (!ReferenceEquals(_running, run)) return;
                _running = null;
                if (t.IsCompletedSuccessfully) Apply(root, search, t.Result);
            }), TaskScheduler.Default);
    }

    /// <summary>Stop the running search, leaving the query and the results already shown.</summary>
    public void StopSearch()
    {
        if (_running is not { } run) return;
        run.Cts.Cancel();
        _running = null;
        var files = Volatile.Read(ref run.FilesSearched);
        _status.Text = $"Stopped · {files:N0} {Plural.Of(files, "file")} searched";
    }

    private void ShowProgress(Running run)
    {
        var files = Volatile.Read(ref run.FilesSearched);
        _status.Text = $"Finding… {files:N0} {Plural.Of(files, "file")}" + (StopKey is { } key ? $" · {key} stop" : string.Empty);
    }

    private static async Task<WorkspaceSearchResult> FindAsync(
        IDirectoryInfo root, Request search, IReadOnlyDictionary<string, string>? buffers, IGitCli? git, Running run)
    {
        var token = run.Cts.Token;
        var files = git is null ? null : (await git.ListFilesAsync(root.FullName, search.IncludeIgnored, token)).Value;
        return WorkspaceSearch.Search(root, search.Query, search.Options, buffers, files,
            fileSearched: () => Interlocked.Increment(ref run.FilesSearched), cancellationToken: token);
    }

    /// <summary>
    /// Replace every result with the replacement text. Destructive across many files with no undo, so
    /// the first call only arms a confirmation; a second call with the same query/replacement/result
    /// count performs it.
    /// </summary>
    public void RequestReplaceAll()
    {
        var search = new Request(Query, Options, IncludeIgnored);
        var replacement = Replacement;
        var count = Result.MatchCount;
        // Never replace against results from an older search that a background search hasn't caught up on.
        if (search.Query.Length == 0 || count == 0 || _resultFor != search) return;

        if (_pendingReplace != (search, replacement, count))
        {
            _pendingReplace = (search, replacement, count);
            _status.Text = $"Replace {count}? Ctrl+Enter again";
            Message?.Invoke(this, $"Replace {count} {Plural.Of(count, "match")} in {Result.Files.Count} {Plural.Of(Result.Files.Count, "file")} with '{replacement}'? Press Ctrl+Enter again to confirm.");
            return;
        }

        _pendingReplace = null;
        var (files, occurrences) = WorkspaceSearch.ReplaceAll(Result.Files.Select(f => f.File), search.Query, replacement, search.Options, OpenBuffers);
        RunSearch();
        Message?.Invoke(this, $"Replaced {occurrences} {Plural.Of(occurrences, "occurrence")} in {files} {Plural.Of(files, "file")}");
    }

    private void Apply(IDirectoryInfo? root, Request search, WorkspaceSearchResult result, string? status = null)
    {
        Result = result;
        _resultFor = search;
        if (root is not null && search.Query.Length > 0 && InRepository(root)) _gitFailed = result.NotARepo;
        _ignoredFiles.Enabled = IgnoredFilesAvailable;
        _results.ClearObjects();
        if (root is not null && result.Files.Count > 0)
        {
            _results.AddObjects(SearchResultTree.Build(root, result));
            _results.ExpandAll();
        }

        _status.Text = status ?? result switch
        {
            { Files.Count: 0 } => "No results",
            { Truncated: true } => $"{result.MatchCount}+ in {result.Files.Count} {Plural.Of(result.Files.Count, "file")}",
            _ => $"{result.MatchCount} in {result.Files.Count} {Plural.Of(result.Files.Count, "file")}",
        } + (result.NotARepo ? " (not a git repo)" : string.Empty);
        SetNeedsDraw();
    }

    private SearchNode? FirstMatch()
    {
        var node = _results.Objects?.FirstOrDefault();
        while (node is not null and not MatchNode)
            node = node.Children.FirstOrDefault();
        return node;
    }

    private void ActivateSelected()
    {
        switch (_results.SelectedObject)
        {
            case MatchNode match:
                MatchActivated?.Invoke(this, (match.File, match.Line.Match));
                break;
            case FileNode file when file.Result.Matches.Count > 0:
                MatchActivated?.Invoke(this, (file.Result.File, file.Result.Matches[0].Match));
                break;
            case { } node:
                _results.Toggle(node);
                break;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // Nulling the run also stops a search that already finished from applying to a dead view.
            _running?.Cts.Cancel();
            _running = null;
        }
        base.Dispose(disposing);
    }
}
