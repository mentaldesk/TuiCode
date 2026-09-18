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
    private const int LabelWidth = 5;

    private readonly TextField _query;
    private readonly Label _replaceLabel;
    private readonly TextField _replacement;
    private readonly Label _status;
    private readonly TreeView<SearchNode> _results;
    private CancellationTokenSource? _searchCts;
    private (string Query, string Replacement, int Matches)? _pendingReplace;
    // The query Result was computed for — lags Query while a background search is in flight.
    private string _resultQuery = string.Empty;

    public event EventHandler<(IFileInfo File, TextMatch Match)>? MatchActivated;

    /// <summary>Longer human-readable outcome (e.g. after replace-all), for the status bar.</summary>
    public event EventHandler<string>? Message;

    public Func<IDirectoryInfo?>? RootProvider { get; set; }
    public IOpenBuffers? OpenBuffers { get; set; }

    public WorkspaceSearchResult Result { get; private set; } = WorkspaceSearchResult.Empty;
    public bool ReplaceVisible => _replacement.Visible;
    public bool InputsHaveFocus => _query.HasFocus || _replacement.HasFocus;
    public bool ReplacementHasFocus => _replacement.HasFocus;
    public string StatusText => _status.Text;

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

        var queryLabel = new Label { X = 0, Y = 0, Text = "Find" };
        _query = new TextField { X = LabelWidth, Y = 0, Width = Dim.Fill() };
        _replaceLabel = new Label { X = 0, Y = 1, Text = "Repl", Visible = false };
        _replacement = new TextField { X = LabelWidth, Y = 1, Width = Dim.Fill(), Visible = false };
        _status = new Label { X = 0, Y = 1, Width = Dim.Fill(), Text = string.Empty };
        _results = new TreeView<SearchNode>
        {
            X = 0,
            Y = 2,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            TreeBuilder = new DelegateTreeBuilder<SearchNode>(n => n.Children, n => n.Children.Count > 0),
        };

        Add(queryLabel, _query, _replaceLabel, _replacement, _status, _results);

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
        _results.Activated += (_, _) => ActivateSelected();
        // Same TG quirk as the explorer: Enter maps to Command.Activate but doesn't raise Activated.
        _results.KeyDown += (_, key) =>
        {
            if (key != Key.Enter) return;
            ActivateSelected();
            key.Handled = true;
        };
    }

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
        _status.Y = visible ? 2 : 1;
        _results.Y = visible ? 3 : 2;
        SetNeedsLayout();
    }

    /// <summary>Re-run the current query (e.g. after files changed on disk or a folder was opened).</summary>
    public void RunSearch()
    {
        _searchCts?.Cancel();
        _searchCts = null;

        var query = Query;
        var root = RootProvider?.Invoke();
        if (query.Length == 0 || root is null)
        {
            Apply(root, query, WorkspaceSearchResult.Empty, root is null && query.Length > 0 ? "No folder open" : string.Empty);
            return;
        }

        // Snapshot open buffers here, on the UI thread — TextView isn't safe to read from the worker.
        var buffers = OpenBuffers?.Snapshot();
        if (App is not { } app)
        {
            Apply(root, query, WorkspaceSearch.Search(root, query, buffers));
            return;
        }

        var cts = _searchCts = new CancellationTokenSource();
        _status.Text = "Finding…";
        Task.Run(() => WorkspaceSearch.Search(root, query, buffers, cancellationToken: cts.Token), cts.Token)
            .ContinueWith(t =>
            {
                if (!t.IsCompletedSuccessfully) return;
                app.Invoke(() =>
                {
                    if (ReferenceEquals(_searchCts, cts)) Apply(root, query, t.Result);
                });
            }, TaskScheduler.Default);
    }

    /// <summary>
    /// Replace every result with the replacement text. Destructive across many files with no undo, so
    /// the first call only arms a confirmation; a second call with the same query/replacement/result
    /// count performs it.
    /// </summary>
    public void RequestReplaceAll()
    {
        var query = Query;
        var replacement = Replacement;
        var count = Result.MatchCount;
        // Never replace against results from an older query that a background search hasn't caught up on.
        if (query.Length == 0 || count == 0 || _resultQuery != query) return;

        if (_pendingReplace != (query, replacement, count))
        {
            _pendingReplace = (query, replacement, count);
            _status.Text = $"Replace {count}? Ctrl+Enter again";
            Message?.Invoke(this, $"Replace {count} matches in {Result.Files.Count} files with '{replacement}'? Press Ctrl+Enter again to confirm.");
            return;
        }

        _pendingReplace = null;
        var (files, occurrences) = WorkspaceSearch.ReplaceAll(Result.Files.Select(f => f.File), query, replacement, OpenBuffers);
        RunSearch();
        Message?.Invoke(this, $"Replaced {occurrences} occurrences in {files} files");
    }

    private void Apply(IDirectoryInfo? root, string query, WorkspaceSearchResult result, string? status = null)
    {
        Result = result;
        _resultQuery = query;
        _results.ClearObjects();
        if (root is not null && result.Files.Count > 0)
        {
            _results.AddObjects(SearchResultTree.Build(root, result));
            _results.ExpandAll();
        }

        _status.Text = status ?? result switch
        {
            { Files.Count: 0 } => "No results",
            { Truncated: true } => $"{result.MatchCount}+ in {result.Files.Count} files",
            _ => $"{result.MatchCount} in {result.Files.Count} files",
        };
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
            // Nulling the token source also stops a search that already finished from applying to a dead view.
            _searchCts?.Cancel();
            _searchCts = null;
        }
        base.Dispose(disposing);
    }
}
