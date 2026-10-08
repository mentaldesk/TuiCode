using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;
using TuiCode.Abstractions;
using TuiCode.Editor;
using TuiCode.Workbench.Find;
using TuiCode.Workbench.Services;

namespace TuiCode.Tests;

// Find in a diff tab (#413), driven through the controller.
public class DiffFindTests : IDisposable
{
    private static readonly DateTimeOffset Posted = new(2026, 9, 20, 6, 54, 0, TimeSpan.Zero);

    private readonly MockFileSystem _fs = new();
    private readonly EditorGroup _group = new();
    private readonly InputScopeStack _scopes = new();
    private readonly KeybindingService _workbenchScope = new(new CommandService());
    private readonly FindController _find;

    public DiffFindTests()
    {
        _scopes.Push(_workbenchScope);
        _find = new FindController(_group, _scopes, _workbenchScope);
        _group.ActiveTabChanged += (_, _) => _find.OnActiveTabChanged();
    }

    [Fact]
    public void Matches_go_down_the_rows_left_side_before_right_and_wrap()
    {
        var diff = Diff("a culture\nsame\nold culture", "b Culture\nsame\nnew culture CULTURE");

        _find.Open(replace: false);
        _find.Bar.Query = "culture";
        var visited = new List<DiffMatch?> { diff.CurrentMatch };
        for (var i = 0; i < 5; i++)
        {
            _find.Next();
            visited.Add(diff.CurrentMatch);
        }

        Assert.Equal(
        [
            new DiffMatch(0, DiffSide.Left, 2, 7),
            new DiffMatch(0, DiffSide.Right, 2, 7),
            new DiffMatch(2, DiffSide.Left, 4, 7),
            new DiffMatch(2, DiffSide.Right, 4, 7),
            new DiffMatch(2, DiffSide.Right, 12, 7),
            new DiffMatch(0, DiffSide.Left, 2, 7),
        ], visited);
        _find.Previous();
        Assert.Equal(new DiffMatch(2, DiffSide.Right, 12, 7), diff.CurrentMatch);
        Assert.Equal("5 of 5", _find.Bar.Status);
    }

    [Fact]
    public void A_row_on_one_side_only_is_matched_on_that_side_and_its_gap_is_not()
    {
        var diff = Diff("find\nkeep", "keep\nfind");

        _find.Open(replace: false);
        _find.Bar.Query = "find";
        var first = diff.CurrentMatch;
        _find.Next();

        Assert.Equal(2, _find.Matches.Count);
        Assert.Equal(new DiffMatch(0, DiffSide.Left, 0, 4), first);
        Assert.Equal(new DiffMatch(2, DiffSide.Right, 0, 4), diff.CurrentMatch);
    }

    [Fact]
    public void Typing_searches_from_the_current_row_and_refining_does_not_run_ahead()
    {
        var diff = Diff("x one\nx two\nx three\nxy four", "x one\nx two\nx three\nxy FOUR");
        diff.NewKeyDownEvent(Key.CursorDown);

        _find.Open(replace: false);
        _find.Bar.Query = "x";
        Assert.Equal(new DiffMatch(1, DiffSide.Left, 0, 1), diff.CurrentMatch);
        Assert.Equal(1, diff.CurrentRow);

        _find.Bar.Query = "x ";
        Assert.Equal(new DiffMatch(1, DiffSide.Left, 0, 2), diff.CurrentMatch);
        Assert.Equal("3 of 6", _find.Bar.Status);
    }

    [Fact]
    public void The_count_reads_as_it_does_in_a_file()
    {
        Diff("one\ntwo", "one\nTWO");
        _find.Open(replace: false);

        _find.Bar.Query = "o";
        Assert.Equal("1 of 4", _find.Bar.Status);

        _find.Bar.Query = "zzz";
        Assert.Equal("No results", _find.Bar.Status);
    }

    [Fact]
    public void Help_in_a_diff_lists_no_replace_keys_whichever_field_last_had_them()
    {
        Diff("one", "two");
        _find.Open(replace: true);
        _find.Bar.KeysInReplacement = true;

        Assert.Equal(
        [
            new("Enter", "Next match"),
            new("Shift+Enter", "Previous match"),
            new("Esc", "Close"),
        ], _find.Help().Rows);
    }

    [Fact]
    public void Next_moves_the_diff_s_current_row_to_the_match()
    {
        var diff = Diff("a\nb\nc\nneedle", "A\nb\nc\nneedle");
        _find.Open(replace: false);
        _find.Bar.Query = "needle";

        Assert.Equal(3, diff.CurrentRow);
        Assert.Equal(3, diff.CurrentBufferLine);
    }

    [Fact]
    public void Closing_leaves_the_current_row_on_the_last_match_and_clears_the_marks()
    {
        var diff = Diff("a\nneedle\nb\nneedle", "A\nneedle\nb\nneedle");
        _find.Open(replace: false);
        _find.Bar.Query = "needle";
        _find.Next();
        _find.Next();

        _find.Close();

        Assert.Equal(3, diff.CurrentRow);
        Assert.Null(diff.CurrentMatch);
        Assert.Null(_find.Bar.SuperView);
    }

    [Fact]
    public void Replace_is_not_offered_in_a_diff()
    {
        var diff = Diff("cat", "cats");

        _find.Open(replace: true);
        _find.Bar.Query = "cat";
        _find.ReplaceAll();

        Assert.False(_find.Bar.ReplaceVisible);
        Assert.Equal("cats", diff.Source!.Lines[0]);
    }

    [Fact]
    public void The_bar_follows_a_switch_between_a_file_and_a_diff_keeping_the_query()
    {
        _fs.AddFile("/work/b.txt", new MockFileData("needle in b"));
        var diff = Diff("needle", "needle needle");
        var file = _group.OpenOrFocus(_fs.FileInfo.New("/work/b.txt"));
        _find.Open(replace: true);
        _find.Bar.Query = "needle";

        _group.Value = diff;
        Assert.Same(diff, _find.Bar.SuperView);
        Assert.Equal("needle", _find.Bar.Query);
        Assert.False(_find.Bar.ReplaceVisible);
        Assert.Equal(3, _find.Matches.Count);

        _group.Value = file;
        Assert.Same(file, _find.Bar.SuperView);
        Assert.Single(_find.Matches);
    }

    [Fact]
    public void Review_thread_text_is_not_searched_but_the_code_around_it_is()
    {
        var diff = Diff("one\ntwo", "one\ntwo needle");
        diff.ShowThreads([new GitHubReviewThread("a.txt", 2, false, false, [new GitHubComment("octocat", Posted, "needle here too")])]);

        _find.Open(replace: false);
        _find.Bar.Query = "needle";

        Assert.Single(_find.Matches);
        Assert.Equal(new DiffMatch(1, DiffSide.Right, 4, 6), diff.CurrentMatch);
    }

    [Fact]
    public void Matches_refresh_with_the_diff()
    {
        var diff = Diff("one", "two");
        _find.Open(replace: false);
        _find.Bar.Query = "needle";
        Assert.Empty(_find.Matches);

        diff.Source!.Content = "two needle";
        diff.Refresh();

        Assert.Single(_find.Matches);
    }

    [Fact]
    public void Closing_the_diff_with_the_bar_on_it_keeps_the_bar_for_the_next_tab()
    {
        var diff = Diff("needle", "needle!");
        _find.Open(replace: false);
        _find.Bar.Query = "needle";

        _group.CloseDiff(diff);

        Assert.Same(diff.Source, _find.Bar.SuperView);
        _find.Bar.Query = "need";
        Assert.Single(_find.Matches);
    }

    private DiffTab Diff(string saved, string buffer)
    {
        _fs.AddFile("/work/a.txt", new MockFileData(saved));
        var source = _group.OpenOrFocus(_fs.FileInfo.New("/work/a.txt"));
        source.Content = buffer;
        return _group.Compare(source, "saved", () => DiffTab.ReadLines(source.File))!;
    }

    public void Dispose()
    {
        _find.Dispose();
        _group.Dispose();
    }
}

// Renders through a TG driver — serialised (#77).
public class DiffFindDrawTests : StaticConfigurationTest
{
    private readonly IApplication _app = Application.Create().Init(DriverRegistry.Names.ANSI);
    private readonly MockFileSystem _fs = new();

    public DiffFindDrawTests() => _app.Driver!.SetScreenSize(40, 12);

    public override void Dispose()
    {
        _app.Dispose();
        base.Dispose();
    }

    [Fact]
    public void Matches_are_highlighted_over_the_tints_and_changed_words_and_the_current_one_like_a_selection()
    {
        var diff = Diff("Log(a, count);\nkeep", "Log(a, total);\nkeep");
        Render(diff);
        Assert.Equal(DiffTab.DefaultRemovedText, BackgroundAt(1, 12));
        diff.SetHighlights([new DiffMatch(0, DiffSide.Left, 7, 5), new DiffMatch(0, DiffSide.Right, 0, 3), new DiffMatch(0, DiffSide.Right, 7, 5)]);
        diff.ShowMatch(new DiffMatch(0, DiffSide.Right, 7, 5));

        Render(diff);

        var highlight = diff.GetAttributeForRole(VisualRole.Highlight);
        var selected = diff.GetAttributeForRole(VisualRole.Active);
        Assert.All(Enumerable.Range(12, 5), col => Assert.Equal(highlight, AttributeAt(1, col)));
        Assert.NotEqual(highlight, AttributeAt(1, 17));
        Assert.All(Enumerable.Range(24, 3), col => Assert.Equal(highlight, AttributeAt(1, col)));
        Assert.NotEqual(highlight, AttributeAt(1, 27));
        Assert.All(Enumerable.Range(31, 5), col => Assert.Equal(selected, AttributeAt(1, col)));
        Assert.NotEqual(selected, AttributeAt(1, 36));
    }

    [Fact]
    public void Showing_a_match_off_screen_below_and_to_the_right_scrolls_it_into_view()
    {
        var lines = Enumerable.Range(1, 30).Select(i => $"line {i}").ToList();
        lines[24] = new string('x', 60) + "needle";
        var text = string.Join('\n', lines);
        var diff = Diff(text, text);

        diff.ShowMatch(new DiffMatch(24, DiffSide.Right, 60, 6));
        Render(diff);

        Assert.Equal(24, diff.CurrentRow);
        Assert.InRange(24, diff.TopRow, diff.TopRow + 8);
        Assert.True(diff.LeftColumn > 0);
        var y = 24 - diff.TopRow + 1;
        var selected = Enumerable.Range(0, diff.Frame.Width)
            .Where(col => AttributeAt(y, col) == diff.GetAttributeForRole(VisualRole.Active))
            .Select(col => _app.Driver!.Contents![y, col].Grapheme);
        Assert.EndsWith("needle", string.Concat(selected));

        diff.ShowMatch(new DiffMatch(0, DiffSide.Left, 0, 4));
        Assert.Equal((0, 0, 0), (diff.CurrentRow, diff.TopRow, diff.LeftColumn));
    }

    [Fact]
    public void A_docked_header_pushes_both_sides_down()
    {
        var diff = Diff("one", "ONE");

        diff.SetHeader(new View { Height = 2 });
        var screen = Render(diff);

        Assert.Equal(" saved".PadRight(18) + "│" + " working copy".PadRight(18), screen[2]);
        Assert.Equal("  1- one".PadRight(18) + "│" + "  1+ ONE".PadRight(18), screen[3]);
    }

    private DiffTab Diff(string saved, string buffer)
    {
        _fs.AddFile("/work/a.txt", new MockFileData(saved));
        var source = new EditorTab(_fs.FileInfo.New("/work/a.txt")) { Content = buffer };
        var diff = new DiffTab(source, "saved", () => DiffTab.ReadLines(source.File))
        {
            App = _app,
            Width = 37,
            Height = 10,
        };
        diff.BeginInit();
        diff.EndInit();
        diff.Layout();
        diff.Refresh();
        return diff;
    }

    private Color BackgroundAt(int row, int col) => AttributeAt(row, col).Background;

    private Terminal.Gui.Drawing.Attribute AttributeAt(int row, int col) => _app.Driver!.Contents![row, col].Attribute!.Value;

    private string[] Render(DiffTab view)
    {
        var driver = _app.Driver!;
        driver.ClearContents();
        driver.Clip = new Region(driver.Screen);
        view.Layout();
        view.SetNeedsDraw();
        view.Draw();
        return Enumerable.Range(0, view.Frame.Height)
            .Select(row => string.Concat(Enumerable.Range(0, view.Frame.Width).Select(col => driver.Contents![row, col].Grapheme)))
            .ToArray();
    }
}
