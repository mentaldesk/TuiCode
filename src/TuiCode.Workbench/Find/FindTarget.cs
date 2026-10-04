using TuiCode.Abstractions;
using TuiCode.Editor;
using TuiCode.Search;

namespace TuiCode.Workbench.Find;

/// <summary>What the find bar searches: a file tab's buffer, or both sides of a diff (#413).</summary>
internal abstract class FindTarget
{
    public abstract View View { get; }
    public abstract bool CanReplace { get; }
    public abstract bool ContentHasFocus { get; }

    /// <summary>Where searching starts, comparable with the positions of <see cref="FindAll"/>'s matches.</summary>
    public abstract (int Row, int Column) Origin { get; }

    /// <summary>A single-line selection to seed the query with, or empty.</summary>
    public abstract string SelectedText { get; }

    public abstract event EventHandler? ContentChanged;

    /// <summary>Every match in reading order.</summary>
    public abstract IReadOnlyList<TextMatch> FindAll(string query);

    public abstract void SetHighlights(IReadOnlyList<TextMatch> matches);
    public abstract void Select(TextMatch match);
    public abstract void ClearSelection();
    public abstract void Replace(TextMatch match, string replacement);
    public abstract void SetHeader(View? header);

    public static FindTarget? For(View? view) => view switch
    {
        EditorTab tab => new EditorTarget(tab),
        DiffTab diff => new DiffTarget(diff),
        _ => null,
    };

    private sealed class EditorTarget(EditorTab tab) : FindTarget
    {
        public override View View => tab;
        public override bool CanReplace => true;
        public override bool ContentHasFocus => tab.ContentHasFocus;
        public override (int Row, int Column) Origin => tab.SelectionOrigin;
        public override string SelectedText => tab.SelectedText;

        public override event EventHandler? ContentChanged
        {
            add => tab.ContentChanged += value;
            remove => tab.ContentChanged -= value;
        }

        public override IReadOnlyList<TextMatch> FindAll(string query) => TextSearch.FindAll(tab.Lines, query);
        public override void SetHighlights(IReadOnlyList<TextMatch> matches) => tab.SetHighlights(matches);
        public override void Select(TextMatch match) => tab.Select(match);
        public override void ClearSelection() => tab.ClearSelection();
        public override void Replace(TextMatch match, string replacement) => tab.Replace(match, replacement);
        public override void SetHeader(View? header) => tab.SetHeader(header);
    }

    /// <summary>
    /// A match's <see cref="TextMatch.Row"/> here is two per row of the diff, left side first, so the
    /// controller's row-then-column order is the diff's reading order.
    /// </summary>
    private sealed class DiffTarget(DiffTab diff) : FindTarget
    {
        public override View View => diff;
        public override bool CanReplace => false;
        public override bool ContentHasFocus => diff.IsFocused;
        public override (int Row, int Column) Origin => (diff.CurrentDiffRow * 2, 0);
        public override string SelectedText => string.Empty;

        public override event EventHandler? ContentChanged
        {
            add => diff.Refreshed += value;
            remove => diff.Refreshed -= value;
        }

        public override IReadOnlyList<TextMatch> FindAll(string query)
        {
            var matches = new List<TextMatch>();
            if (query.Length == 0) return matches;
            var rows = diff.Diff.Rows;
            for (var i = 0; i < rows.Count; i++)
            {
                if (rows[i].Left is { } left) Add(matches, i * 2, diff.LeftLines[left], query);
                if (rows[i].Right is { } right) Add(matches, i * 2 + 1, diff.RightLines[right], query);
            }
            return matches;
        }

        private static void Add(List<TextMatch> matches, int row, string line, string query)
        {
            foreach (var match in TextSearch.FindAll([line], query))
                matches.Add(match with { Row = row });
        }

        public override void SetHighlights(IReadOnlyList<TextMatch> matches) => diff.SetHighlights(matches.Select(ToDiff));
        public override void Select(TextMatch match) => diff.ShowMatch(ToDiff(match));
        public override void ClearSelection() => diff.ClearCurrentMatch();
        public override void Replace(TextMatch match, string replacement) => throw new NotSupportedException();
        public override void SetHeader(View? header) => diff.SetHeader(header);

        private static DiffMatch ToDiff(TextMatch match) =>
            new(match.Row / 2, match.Row % 2 == 0 ? DiffSide.Left : DiffSide.Right, match.Column, match.Length);
    }
}
