using System.Globalization;
using System.Text;
using Terminal.Gui.Configuration;
using Terminal.Gui.Text;
using TuiCode.Abstractions;
using TuiCode.Icons;
using TuiCode.Syntax;
using TuiCode.Workbench.Git;
using Attribute = Terminal.Gui.Drawing.Attribute;

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
    private readonly FileIcons? _icons;
    private readonly SyntaxHighlighter? _syntax;
    private readonly Label _title;
    private readonly Label _header;
    private readonly TotalsLabel _totals;
    private readonly Label _checks;
    private readonly Label _threadCounts;
    private readonly Label _hint;
    private readonly Label _draftReview;
    private readonly Label _viewed;
    private readonly Button _overview;
    private readonly Line _rule;
    private readonly TreeView<ReviewNode> _files;
    private CancellationTokenSource? _loading;
    private string _headerText = string.Empty;
    private string _totalsText = string.Empty;
    private string _checksText = string.Empty;
    private string _threadsText = string.Empty;
    private string _hintText = string.Empty;

    public event EventHandler<(BranchReview Review, GitChange Change)>? FileActivated;

    /// <summary>Raised by the Overview button (#185), for the Overview tab.</summary>
    public event EventHandler<BranchReview>? PullRequestActivated;

    /// <summary>Raised by Enter on a file's outdated threads (#186), for a tab to read them in.</summary>
    public event EventHandler<(BranchReview Review, ReviewOutdatedNode Node)>? OutdatedThreadsActivated;

    /// <summary>Raised once the PR's review threads are in, so open diffs can show them (#186).</summary>
    public event EventHandler<BranchReview>? ThreadsLoaded;

    /// <summary>Raised after every redraw of the pane, for the workbench to settle focus (#228).</summary>
    public event EventHandler? Refreshed;

    public Func<IDirectoryInfo?>? RootProvider { get; set; }

    public BranchReview? Review { get; private set; }

    public string HeaderText => _header.Text;

    public string TitleText => _title.Text;

    /// <summary>How many files and lines the branch changes (#394).</summary>
    public string TotalsText => _totals.Text;

    public string ChecksText => _checks.Text;

    /// <summary>What the header says about the PR's review threads (#186).</summary>
    public string ThreadsText => _threadCounts.Text;

    public string HintText => _hint.Text;

    /// <summary>What the foot says about the drafted line comments (#188); empty while there are none.</summary>
    public string DraftReviewText => _draftReview.Text;

    /// <summary>What the foot says about viewed files (#396), e.g. <c>Viewed 2 of 5</c>; empty without a PR.</summary>
    public string ViewedText => _viewed.Text;

    /// <summary>Whether <c>tv</c> has something to mark: a file or folder is selected, on a PR whose viewed files have loaded (#396, #399).</summary>
    public bool CanToggleViewed => Review is { PullRequest: not null, Viewed: not null } && _files.SelectedObject is ReviewFileNode or ReviewFolderNode;

    public GitChange? SelectedFile => (_files.SelectedObject as ReviewFileNode)?.Change;

    public string? SelectedFolder => (_files.SelectedObject as ReviewFolderNode)?.Path;

    public bool ListHasFocus => _files.HasFocus;

    /// <summary>The outdated thread the list is on, for <c>cc</c> to reply to (#189); null on any other row.</summary>
    public GitHubReviewThread? SelectedThread => (_files.SelectedObject as ReviewThreadNode)?.Thread;

    /// <summary>Whether the Overview button is selected; it opens the Overview tab (#185).</summary>
    public bool OverviewHasFocus => _overview.HasFocus;

    internal Label Hint => _hint;

    /// <summary>The changed files in the order the tab lists them (#181).</summary>
    public IReadOnlyList<GitChange> ChangedFiles => [.. AllFiles().Select(f => f.Change)];

    internal TreeView<ReviewNode> Files => _files;

    /// <param name="syntax">Its token theme's <c>gitDecoration.*</c> colours tint the change marks.</param>
    public ReviewView(IGitCli? git = null, IGitHubCli? gitHub = null, FileIcons? icons = null, SyntaxHighlighter? syntax = null)
    {
        _git = git ?? new GitCli(new FileSystem());
        _gitHub = gitHub ?? new GitHubCli();
        _icons = icons;
        _syntax = syntax;
        CanFocus = true;

        _title = Line();
        _header = Line();
        _totals = new TotalsLabel(CountColor) { X = 0, Y = 0, Width = Dim.Fill(), Text = string.Empty, Visible = false };
        _checks = Line();
        _threadCounts = Line();
        _hint = Line();
        _draftReview = new Label { X = 0, Y = Pos.AnchorEnd(1), Width = Dim.Fill(), Text = string.Empty, Visible = false };
        _viewed = new Label { X = 0, Y = Pos.AnchorEnd(1), Width = Dim.Fill(), Text = string.Empty, Visible = false };
        // GetAttributeForRole isn't virtual in TG 2.1.0, so the hint is styled through its event; Handled makes the result stick.
        _hint.GettingAttributeForRole += (_, e) =>
        {
            var attribute = e.Result ?? GetAttributeForRole(e.Role);
            e.Result = attribute with { Style = attribute.Style | TextStyle.Faint };
            e.Handled = true;
        };
        _overview = new Button { X = 0, Y = 0, Height = 1, Text = "Overview", Visible = false, ShadowStyle = ShadowStyles.None };
        _overview.Accepting += (_, e) =>
        {
            e.Handled = true;
            if (Review is { PullRequest: not null } review) PullRequestActivated?.Invoke(this, review);
        };
        _rule = new Line { X = 0, Y = 0, Width = Dim.Fill(), Visible = false };
        _files = new TreeView<ReviewNode>
        {
            X = 0,
            Y = 1,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            Visible = false,
            TreeBuilder = new DelegateTreeBuilder<ReviewNode>(n => n.Children, n => n.Children.Count > 0),
        };
        _files.AspectGetter = node => ReviewRow.Display(node, ThreadIcon(node) is not null);
        _files.DrawLine += (_, e) =>
        {
            if (e.Model is ReviewFileNode file) DrawFile(e, file);
            else if (e.Model is ReviewFolderNode folder) DrawFolder(e, folder);
        };
        if (icons is not null) icons.Changed += (_, _) => _files.SetNeedsDraw();
        Add(_title, _header, _totals, _checks, _threadCounts, _hint, _overview, _rule, _files, _viewed, _draftReview);

        ViewportChanged += (_, _) => LayoutHeader();
        _files.Activated += (_, _) => ActivateSelected();
        // Same TG quirk as the explorer: Enter maps to Command.Activate but doesn't raise Activated.
        _files.KeyDown += (_, key) =>
        {
            if (key == Key.CursorUp && _overview.Visible && ReferenceEquals(_files.SelectedObject, FirstNode()))
            {
                _overview.SetFocus();
                key.Handled = true;
                return;
            }
            if (key != Key.Enter) return;
            ActivateSelected();
            key.Handled = true;
        };
        _overview.KeyDown += (_, key) =>
        {
            if (key != Key.CursorDown || !_files.Visible) return;
            FocusList();
            key.Handled = true;
        };
    }

    private static Label Line() => new() { X = 0, Y = 0, Width = Dim.Fill(), Text = string.Empty, Visible = false };

    /// <summary>
    /// Lays out a file's row: what the branch did to it (#320) and its type icon (#321), its name, any thread badge (#186),
    /// and its line counts at the right edge (#393), cutting the name rather than the counts when they don't all fit.
    /// </summary>
    private void DrawFile(DrawTreeViewLineEventArgs<ReviewNode> e, ReviewFileNode file)
    {
        var start = e.IndexOfModelText;
        if (e.Cells is not { } cells || start < 0 || start >= cells.Count) return;

        var row = cells[start].Attribute ?? default;
        var change = _icons?.ForChange(file.Change.Kind, _syntax?.EditorColors);
        var mark = change ?? new FileIcon(file.Mark.ToString());
        var icons = _icons?.ForFile(file.Name) is { } type ? [mark, type] : new[] { mark };
        var faint = file.Viewed || (change is not null && file.Change.Kind == GitChangeKind.Deleted);
        var name = faint ? Styled(row, TextStyle.Faint) : row;

        var tail = new List<Cell>();
        if (file.Viewed)
        {
            var check = _icons?.ForViewed() ?? new FileIcon(ReviewRow.ViewedMark);
            tail.AddRange([.. CellsOf("  ", row), new Cell { Grapheme = check.Glyph, Attribute = IconDrawing.AttributeFor(check, row) }]);
        }
        else if (file.Changed)
            tail.AddRange([.. CellsOf("  ", row), .. CellsOf(ReviewRow.ChangedMark, Accented(row))]);
        var chat = ThreadIcon(file);
        if (file.Badge(chat is not null) is { } badge)
        {
            var style = Styled(row, file.BadgeStyle);
            tail.AddRange(CellsOf("  ", row));
            if (chat is { } glyph) tail.AddRange([new Cell { Grapheme = glyph.Glyph, Attribute = Styled(IconDrawing.AttributeFor(glyph, row), file.BadgeStyle) }, Space(row)]);
            tail.AddRange(CellsOf(badge, style));
        }

        var counts = new List<Cell>();
        foreach (var (text, color) in file.Counts)
        {
            if (counts.Count > 0) counts.Add(Space(row));
            counts.AddRange(CellsOf(text, color is { } kind ? CountColor(kind, text, row) : Styled(row, TextStyle.Faint)));
        }

        var lead = cells.Take(start).ToList();
        var used = Columns(lead) + icons.Sum(i => i.Glyph.GetColumns() + 1) + Columns(tail) + Columns(counts);
        var label = file.Name;
        var gap = counts.Count > 0 ? 1 : 0;
        if (counts.Count > 0 && used + gap + label.GetColumns() > _files.Viewport.Width)
            label = Cut(label, Math.Max(1, _files.Viewport.Width - used - gap));
        var pad = Math.Max(gap, _files.Viewport.Width - used - label.GetColumns());

        cells.Clear();
        cells.AddRange([.. lead, .. CellsOf(label, name), .. tail, .. Enumerable.Repeat(Space(row), pad), .. counts]);
        foreach (var icon in icons.Reverse()) IconDrawing.Prepend(e, icon);
    }

    private void DrawFolder(DrawTreeViewLineEventArgs<ReviewNode> e, ReviewFolderNode folder)
    {
        if (folder.Viewed && e.Cells is { } cells)
            for (var i = Math.Max(e.IndexOfModelText, 0); i < cells.Count; i++)
                cells[i] = cells[i] with { Attribute = Styled(cells[i].Attribute ?? default, TextStyle.Faint) };
        if (_icons?.ForDirectory(_files.IsExpanded(folder)) is { } icon) IconDrawing.Prepend(e, icon);
    }

    // Accent's Normal foreground is the plain text colour in most themes; HotNormal's is the one that stands out.
    private static Attribute Accented(Attribute row) =>
        SchemeManager.TryGetScheme("Accent", out var accent) ? row with { Foreground = accent.HotNormal.Foreground } : row;

    private Attribute CountColor(GitChangeKind kind, string text, Attribute row)
    {
        var (dark, light) = FileIcons.ChangeColors(kind, _syntax?.EditorColors);
        return IconDrawing.AttributeFor(new FileIcon(text, dark, light), row);
    }

    /// <summary>The totals line, its <c>+</c> count in the added colour and its <c>−</c> count in the deleted one.</summary>
    private sealed class TotalsLabel(Func<GitChangeKind, string, Attribute, Attribute> countColor) : Label
    {
        protected override bool OnDrawingText(DrawContext? context)
        {
            var normal = GetAttributeForRole(VisualRole.Normal);
            GitChangeKind? kind = null;
            Move(0, 0);
            var enumerator = StringInfo.GetTextElementEnumerator(Text);
            while (enumerator.MoveNext())
            {
                var grapheme = enumerator.GetTextElement();
                kind = grapheme switch
                {
                    "+" => GitChangeKind.Added,
                    "−" => GitChangeKind.Deleted,
                    " " => null,
                    _ => kind,
                };
                SetAttribute(kind is { } k ? countColor(k, grapheme, normal) : normal);
                AddStr(grapheme);
            }
            return true;
        }
    }

    private static string Cut(string text, int columns)
    {
        var kept = new StringBuilder();
        var enumerator = StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext() && kept.ToString().GetColumns() + enumerator.GetTextElement().GetColumns() <= columns - 1)
            kept.Append(enumerator.GetTextElement());
        return kept.Append('…').ToString();
    }

    private static int Columns(IEnumerable<Cell> cells) => cells.Sum(c => Math.Max(1, c.Grapheme.GetColumns()));

    private static Cell Space(Attribute attribute) => new() { Grapheme = " ", Attribute = attribute };

    private static IEnumerable<Cell> CellsOf(string text, Attribute attribute)
    {
        var enumerator = StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext()) yield return new Cell { Grapheme = enumerator.GetTextElement(), Attribute = attribute };
    }

    private FileIcon? ThreadIcon(ReviewNode node) =>
        node is ReviewFileNode { Threads.Count: > 0 } file ? _icons?.ForThreads(file.UnresolvedCount > 0) : null;

    private static Attribute Styled(Attribute attribute, TextStyle style) => attribute with { Style = attribute.Style | style };

    /// <summary>Shows what the review carries in drafts (#188), at the foot of the tab under the file list.</summary>
    public void ShowDraftReview(string line)
    {
        _draftReview.Text = line;
        _draftReview.Visible = line.Length > 0;
        LayoutFoot();
    }

    /// <summary>Stacks the foot's lines under the file list, <c>Viewed n of m</c> above the draft review.</summary>
    private void LayoutFoot()
    {
        var text = Review?.ViewedLine ?? string.Empty;
        _viewed.Text = text;
        _viewed.Visible = text.Length > 0;
        _viewed.Y = Pos.AnchorEnd(_draftReview.Visible ? 2 : 1);
        var rows = (_viewed.Visible ? 1 : 0) + (_draftReview.Visible ? 1 : 0);
        _files.Height = rows > 0 ? Dim.Fill(rows) : Dim.Fill();
        SetNeedsDraw();
    }

    /// <summary>Shows <paramref name="paths"/> marked viewed, or not, once GitHub has the marks (#396).</summary>
    public void SetViewed(IEnumerable<string> paths, bool viewed)
    {
        if (Review is not { Viewed: not null } review) return;
        Show(GitResult<BranchReview?>.Success(review.WithViewed(paths, viewed)));
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
        if (Review is null && _headerText.Length == 0)
        {
            _headerText = "Loading…";
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

            // After the list, so counting every line never holds it up (#393).
            review = await CountLinesAsync(app, cts, review).ConfigureAwait(false);

            var pullRequest = await Task.Run(() => BranchReview.WithPullRequestAsync(_git, _gitHub, review, cts.Token), cts.Token).ConfigureAwait(false);
            Apply(app, cts, () => ShowPullRequest(pullRequest));
            if (pullRequest.Value is { } relisted && relisted.MergeBase != review.MergeBase)
                await CountLinesAsync(app, cts, relisted).ConfigureAwait(false);
            if (pullRequest.Value?.PullRequest is not { } pr) return;

            // Last, in its own step: the file list and the header are worth having before the threads are in (#186).
            var state = await Task.Run(() => _gitHub.GetReviewStateAsync(review.RepoRoot, pr.Number, cts.Token), cts.Token).ConfigureAwait(false);
            Apply(app, cts, () => ShowReviewState(state));
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>The review with its line counts, shown once they're in; as it was when git can't count them.</summary>
    private async Task<BranchReview> CountLinesAsync(IApplication? app, CancellationTokenSource cts, BranchReview review)
    {
        var counts = await Task.Run(() => _git.GetLineCountsAsync(review.RepoRoot, review.MergeBase, cts.Token), cts.Token).ConfigureAwait(false);
        if (!counts.Succeeded) return review;

        Apply(app, cts, () =>
        {
            if (Review is { } shown && shown.MergeBase == review.MergeBase)
                Show(GitResult<BranchReview?>.Success(shown with { LineCounts = counts.Value }));
        });
        return review with { LineCounts = counts.Value };
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

    /// <summary>
    /// Swaps a thread for the same one with a reply on it (#189), so the tab shows it without refetching;
    /// the row the reply was written from stays selected.
    /// </summary>
    public void ReplaceThread(GitHubReviewThread thread, GitHubReviewThread updated)
    {
        if (Review is not { } review) return;
        var threads = review.Threads.ToList();
        var index = threads.IndexOf(thread);
        if (index < 0) return;
        threads[index] = updated;
        Show(GitResult<BranchReview?>.Success(review with { Threads = threads }), selectedThread: updated);
    }

    private void Show(GitResult<BranchReview?> result, string notARepo = "Not a git repository", GitHubReviewThread? selectedThread = null)
    {
        var selected = (_files.SelectedObject as ReviewFileNode)?.Change.Path;
        var selectedFolder = SelectedFolder;
        selectedThread ??= SelectedThread;
        Review = result.Value;
        if (result.Value is null) _hintText = string.Empty;

        _files.ClearObjects();
        if (result.Value is { Changes.Count: > 0 } review)
        {
            _headerText = review.Header;
            _totalsText = review.TotalsLine;
            _files.AddObjects(ReviewTree.Build(review.Changes, review.Threads, review.LineCounts, review.Viewed));
            _files.ExpandAll();
            _files.SelectedObject = FindThread(selectedThread) ?? (ReviewNode?)FindFile(selected) ?? FindFolder(selectedFolder) ?? (ReviewNode?)FirstFile();
            _files.Visible = true;
        }
        else
        {
            _headerText = result.Error ?? (result.Value is { } empty ? $"No changes against {empty.Base}" : notARepo);
            _totalsText = string.Empty;
            _files.Visible = false;
        }
        _checksText = result.Value?.ChecksLine ?? string.Empty;
        _threadsText = result.Value?.ThreadsLine ?? string.Empty;
        LayoutHeader();
        LayoutFoot();
        // Rebuilding the tree can drop Terminal.Gui's focus, so the workbench settles it: a refresh that
        // arrives while the keys are elsewhere must not pull them back here (#228).
        Refreshed?.Invoke(this, EventArgs.Empty);
    }

    private void ShowPullRequest(GitHubResult<BranchReview?> result)
    {
        _hintText = result.Error ?? string.Empty;
        if (result.Value is { } review) Show(GitResult<BranchReview?>.Success(review));
        else LayoutHeader();
    }

    private void ShowReviewState(GitHubResult<GitHubReviewState> result)
    {
        if (Review is not { PullRequest: not null } review) return;
        if (result.Error is { } error)
        {
            _hintText = error;
            LayoutHeader();
            return;
        }

        Show(GitResult<BranchReview?>.Success(review with { Threads = result.Value.Threads, Viewed = result.Value.Viewed }));
        if (Review is { } loaded) ThreadsLoaded?.Invoke(this, loaded);
    }

    /// <summary>
    /// Packs whichever header lines have something to say into the rows above the file list, each cut to the
    /// pane's width: a label wider than the sidebar word-wraps onto a row the header never draws (#244).
    /// </summary>
    private void LayoutHeader()
    {
        var row = 0;
        foreach (var (label, text) in ((Label, string)[])
                 [
                     (_title, Review?.TitleLine ?? string.Empty),
                     (_header, _headerText),
                     (_totals, _totalsText),
                     (_checks, _checksText),
                     (_threadCounts, _threadsText),
                     (_hint, _hintText),
                 ])
        {
            label.Text = BranchReview.Truncate(text, Viewport.Width);
            label.Visible = text.Length > 0;
            if (label.Visible) label.Y = row++;
        }
        _overview.Visible = Review is { PullRequest: not null };
        if (_overview.Visible) _overview.Y = row++;
        else if (_overview.HasFocus) FocusList();
        _rule.Visible = row > 0 && _files.Visible;
        if (_rule.Visible) _rule.Y = row++;
        _files.Y = row;
        SetNeedsDraw();
    }

    private IEnumerable<ReviewFileNode> AllFiles() =>
        (_files.Objects ?? []).SelectMany(n => n is ReviewFileNode file ? [file] : n.Children.OfType<ReviewFileNode>());

    private ReviewFileNode? FirstFile() => AllFiles().FirstOrDefault();

    private ReviewNode? FirstNode() => (_files.Objects ?? []).FirstOrDefault();

    private ReviewThreadNode? FindThread(GitHubReviewThread? thread) =>
        thread is null ? null : AllFiles()
            .SelectMany(f => f.Children.OfType<ReviewOutdatedNode>())
            .SelectMany(o => o.Children.OfType<ReviewThreadNode>())
            .FirstOrDefault(t => t.Thread.Equals(thread));

    private ReviewFolderNode? FindFolder(string? path) =>
        path is null ? null : (_files.Objects ?? []).OfType<ReviewFolderNode>().FirstOrDefault(f => f.Path == path);

    private ReviewFileNode? FindFile(string? path) =>
        path is null ? null : AllFiles().FirstOrDefault(f => f.Change.Path == path);

    private void ActivateSelected()
    {
        switch (_files.SelectedObject)
        {
            case ReviewFileNode file when Review is { } review:
                FileActivated?.Invoke(this, (review, file.Change));
                break;
            case ReviewOutdatedNode outdated when Review is { } review:
                OutdatedThreadsActivated?.Invoke(this, (review, outdated));
                break;
            case ReviewThreadNode thread when Review is { } review:
                OutdatedThreadsActivated?.Invoke(this, (review, thread.Outdated));
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
