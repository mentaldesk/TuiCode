using TuiCode.Abstractions;
using TuiCode.Workbench.Git;

namespace TuiCode.Workbench.Review;

/// <summary>
/// The sidebar's Review tab (#180): the files the current branch changes against its base, grouped
/// by folder. <see cref="Refresh"/> asks git in the background once hosted, then gh for the
/// branch's PR (#183), so the file list shows before the PR header fills in.
/// </summary>
public sealed class ReviewView : View
{
    private readonly IGitCli _git;
    private readonly IGitHubCli _gitHub;
    private readonly Label _title;
    private readonly Label _header;
    private readonly Label _checks;
    private readonly Label _hint;
    private readonly TreeView<ReviewNode> _files;
    private CancellationTokenSource? _loading;

    public event EventHandler<(BranchReview Review, GitChange Change)>? FileActivated;

    public Func<IDirectoryInfo?>? RootProvider { get; set; }

    public BranchReview? Review { get; private set; }

    public string HeaderText => _header.Text;

    public string TitleText => _title.Text;

    public string ChecksText => _checks.Text;

    public string HintText => _hint.Text;

    public bool ListHasFocus => _files.HasFocus;

    internal Label Hint => _hint;

    internal TreeView<ReviewNode> Files => _files;

    public ReviewView(IGitCli? git = null, IGitHubCli? gitHub = null)
    {
        _git = git ?? new GitCli(new FileSystem());
        _gitHub = gitHub ?? new GitHubCli();
        CanFocus = true;

        _title = Line();
        _header = Line();
        _checks = Line();
        _hint = Line();
        // GetAttributeForRole isn't virtual in TG 2.1.0, so the hint is styled through its event; Handled makes the result stick.
        _hint.GettingAttributeForRole += (_, e) =>
        {
            var attribute = e.Result ?? GetAttributeForRole(e.Role);
            e.Result = attribute with { Style = attribute.Style | TextStyle.Faint };
            e.Handled = true;
        };
        _files = new TreeView<ReviewNode>
        {
            X = 0,
            Y = 1,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            Visible = false,
            TreeBuilder = new DelegateTreeBuilder<ReviewNode>(n => n.Children, n => n.Children.Count > 0),
        };
        Add(_title, _header, _checks, _hint, _files);

        ViewportChanged += (_, _) => ShowTitle();
        _files.Activated += (_, _) => ActivateSelected();
        // Same TG quirk as the explorer: Enter maps to Command.Activate but doesn't raise Activated.
        _files.KeyDown += (_, key) =>
        {
            if (key != Key.Enter) return;
            ActivateSelected();
            key.Handled = true;
        };
    }

    private static Label Line() => new() { X = 0, Y = 0, Width = Dim.Fill(), Text = string.Empty, Visible = false };

    /// <summary>Focuses the file list, or the tab itself while there's no list to show.</summary>
    public bool FocusList()
    {
        if (!_files.Visible) return SetFocus();
        _files.SelectedObject ??= FirstFile();
        return _files.SetFocus();
    }

    /// <summary>Re-reads the branch's changes, then its PR. The task completes once both are shown, or at once when hosted.</summary>
    public Task Refresh()
    {
        _loading?.Cancel();
        _loading = null;

        if (RootProvider?.Invoke() is not { } root)
        {
            Show(GitResult<BranchReview?>.Success(null), "No folder open");
            return Task.CompletedTask;
        }

        var cts = _loading = new CancellationTokenSource();
        if (Review is null && _header.Text.Length == 0)
        {
            _header.Text = "Loading…";
            LayoutHeader();
        }
        return LoadAsync(root.FullName, cts);
    }

    private async Task LoadAsync(string folder, CancellationTokenSource cts)
    {
        var app = App;
        try
        {
            var branch = await Task.Run(() => BranchReview.LoadAsync(_git, folder, cts.Token), cts.Token).ConfigureAwait(false);
            Apply(app, cts, () => Show(branch));
            if (branch.Value is not { } review) return;

            var pullRequest = await Task.Run(() => BranchReview.WithPullRequestAsync(_git, _gitHub, review, cts.Token), cts.Token).ConfigureAwait(false);
            Apply(app, cts, () => ShowPullRequest(pullRequest));
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void Apply(IApplication? app, CancellationTokenSource cts, Action show)
    {
        void IfCurrent()
        {
            if (ReferenceEquals(_loading, cts)) show();
        }
        if (app is null) IfCurrent();
        else app.Invoke(IfCurrent);
    }

    private void Show(GitResult<BranchReview?> result, string notARepo = "Not a git repository")
    {
        var selected = (_files.SelectedObject as ReviewFileNode)?.Change.Path;
        var listHadFocus = _files.HasFocus;
        Review = result.Value;
        if (result.Value is null) _hint.Text = string.Empty;

        _files.ClearObjects();
        if (result.Value is { Changes.Count: > 0 } review)
        {
            _header.Text = review.Header;
            _files.AddObjects(ReviewTree.Build(review.Changes));
            _files.ExpandAll();
            _files.SelectedObject = FindFile(selected) ?? FirstFile();
            _files.Visible = true;
            if (HasFocus && !listHadFocus) FocusList();
        }
        else
        {
            _header.Text = result.Error ?? (result.Value is { } empty ? $"No changes against {empty.Base}" : notARepo);
            var refocus = listHadFocus;
            _files.Visible = false;
            if (refocus) SetFocus();
        }
        _checks.Text = result.Value?.ChecksLine ?? string.Empty;
        ShowTitle();
    }

    private void ShowPullRequest(GitHubResult<BranchReview?> result)
    {
        _hint.Text = result.Error ?? string.Empty;
        if (result.Value is { } review) Show(GitResult<BranchReview?>.Success(review));
        else LayoutHeader();
    }

    private void ShowTitle()
    {
        _title.Text = BranchReview.Truncate(Review?.TitleLine ?? string.Empty, Viewport.Width);
        LayoutHeader();
    }

    /// <summary>Packs whichever header lines have something to say into the rows above the file list.</summary>
    private void LayoutHeader()
    {
        var row = 0;
        foreach (var label in (Label[])[_title, _header, _checks, _hint])
        {
            label.Visible = label.Text.Length > 0;
            if (label.Visible) label.Y = row++;
        }
        _files.Y = row;
        SetNeedsDraw();
    }

    private IEnumerable<ReviewFileNode> AllFiles() =>
        (_files.Objects ?? []).SelectMany(n => n is ReviewFileNode file ? [file] : n.Children.OfType<ReviewFileNode>());

    private ReviewFileNode? FirstFile() => AllFiles().FirstOrDefault();

    private ReviewFileNode? FindFile(string? path) =>
        path is null ? null : AllFiles().FirstOrDefault(f => f.Change.Path == path);

    private void ActivateSelected()
    {
        switch (_files.SelectedObject)
        {
            case ReviewFileNode file when Review is { } review:
                FileActivated?.Invoke(this, (review, file.Change));
                break;
            case ReviewFolderNode folder:
                _files.Toggle(folder);
                break;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _loading?.Cancel();
            _loading = null;
        }
        base.Dispose(disposing);
    }
}
