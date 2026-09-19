using TuiCode.Abstractions;
using TuiCode.Workbench.Git;

namespace TuiCode.Workbench.Review;

/// <summary>
/// The sidebar's Review tab (#180): the files the current branch changes against the default
/// branch, grouped by folder. <see cref="Refresh"/> asks git in the background once hosted.
/// </summary>
public sealed class ReviewView : View
{
    private readonly IGitCli _git;
    private readonly Label _header;
    private readonly TreeView<ReviewNode> _files;
    private CancellationTokenSource? _loading;

    public event EventHandler<(BranchReview Review, GitChange Change)>? FileActivated;

    public Func<IDirectoryInfo?>? RootProvider { get; set; }

    public BranchReview? Review { get; private set; }

    public string HeaderText => _header.Text;

    public bool ListHasFocus => _files.HasFocus;

    /// <summary>The changed files in the order the tab lists them (#181).</summary>
    public IReadOnlyList<GitChange> ChangedFiles => [.. AllFiles().Select(f => f.Change)];

    internal TreeView<ReviewNode> Files => _files;

    public ReviewView(IGitCli? git = null)
    {
        _git = git ?? new GitCli(new FileSystem());
        CanFocus = true;

        _header = new Label { X = 0, Y = 0, Width = Dim.Fill(), Text = string.Empty };
        _files = new TreeView<ReviewNode>
        {
            X = 0,
            Y = 1,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            Visible = false,
            TreeBuilder = new DelegateTreeBuilder<ReviewNode>(n => n.Children, n => n.Children.Count > 0),
        };
        Add(_header, _files);

        _files.Activated += (_, _) => ActivateSelected();
        // Same TG quirk as the explorer: Enter maps to Command.Activate but doesn't raise Activated.
        _files.KeyDown += (_, key) =>
        {
            if (key != Key.Enter) return;
            ActivateSelected();
            key.Handled = true;
        };
    }

    /// <summary>Focuses the file list, or the tab itself while there's no list to show.</summary>
    public bool FocusList()
    {
        if (!_files.Visible) return SetFocus();
        _files.SelectedObject ??= FirstFile();
        return _files.SetFocus();
    }

    /// <summary>Moves the selection to <paramref name="path"/> without taking focus; false when it isn't listed.</summary>
    public bool SelectFile(string path)
    {
        if (FindFile(path) is not { } file) return false;
        _files.SelectedObject = file;
        _files.EnsureVisible(file);
        SetNeedsDraw();
        return true;
    }

    /// <summary>Re-reads the branch's changes. The task completes once they're shown, or at once when hosted.</summary>
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
        if (Review is null && _header.Text.Length == 0) _header.Text = "Loading…";
        var app = App;
        return Task.Run(() => BranchReview.LoadAsync(_git, root.FullName, cts.Token), cts.Token)
            .ContinueWith(t =>
            {
                if (!t.IsCompletedSuccessfully) return;
                void Apply()
                {
                    if (ReferenceEquals(_loading, cts)) Show(t.Result);
                }
                if (app is null) Apply();
                else app.Invoke(Apply);
            }, TaskScheduler.Default);
    }

    private void Show(GitResult<BranchReview?> result, string notARepo = "Not a git repository")
    {
        var selected = (_files.SelectedObject as ReviewFileNode)?.Change.Path;
        var listHadFocus = _files.HasFocus;
        Review = result.Value;

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
