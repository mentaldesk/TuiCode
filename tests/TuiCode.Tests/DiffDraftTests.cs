using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;
using TuiCode.Abstractions;
using TuiCode.Editor;

namespace TuiCode.Tests;

// Draft comments in the diff (#188). Renders through a TG driver — serialised (#77).
public class DiffDraftTests : StaticConfigurationTest
{
    private const int Columns = 31;

    private readonly IApplication _app = Application.Create().Init(DriverRegistry.Names.ANSI);
    private readonly MockFileSystem _fs = new();

    public DiffDraftTests() => _app.Driver!.SetScreenSize(40, 10);

    public override void Dispose()
    {
        _app.Dispose();
        base.Dispose();
    }

    [Fact]
    public void A_draft_sits_under_its_line_showing_its_first_line()
    {
        var diff = Diff("one\ntwo\nthree", "one\nTWO\nthree");

        diff.ShowDrafts([new DraftComment("a.txt", 2, "Rename the tuple?\nIt reads badly.")]);

        Assert.Equal(
        [
            "  2- two       │  2+ TWO       ",
            "┃ Draft: Rename the tuple?     ",
            "  3  three     │  3  three     ",
        ], Render(diff)[2..5]);
    }

    [Fact]
    public void A_draft_sits_under_the_threads_on_the_same_line()
    {
        var diff = Diff("one\ntwo", "one\nTWO");
        diff.ShowThreads([new GitHubReviewThread("a.txt", 2, false, false, [new GitHubComment("octocat", default, "Does this hold?")])]);

        diff.ShowDrafts([new DraftComment("a.txt", 2, "Answered below.")]);

        Assert.Equal(
        [
            "┃ octocat: Does this hold?     ",
            "┃ Draft: Answered below.       ",
        ], Render(diff)[3..5]);
    }

    [Fact]
    public void The_current_row_maps_to_the_line_GitHub_numbers_on_the_head_side()
    {
        var diff = Diff("one\ntwo\nthree", "one\nthree");

        Assert.Equal(1, diff.CurrentHeadLine);
        diff.NewKeyDownEvent(Key.CursorDown);
        // Row 2 is `two`, which only the base has: there's no line on the right to comment on.
        Assert.Null(diff.CurrentHeadLine);
        diff.NewKeyDownEvent(Key.CursorDown);
        Assert.Equal(2, diff.CurrentHeadLine);
    }

    [Fact]
    public void A_draft_row_takes_the_line_it_sits_under()
    {
        var diff = Diff("one\ntwo", "one\nTWO");
        diff.ShowDrafts([new DraftComment("a.txt", 2, "Look here.")]);

        diff.NewKeyDownEvent(Key.CursorDown);
        diff.NewKeyDownEvent(Key.CursorDown);

        Assert.Equal("Look here.", diff.CurrentDraft?.Body);
        Assert.Equal(2, diff.CurrentHeadLine);
    }

    [Fact]
    public void A_draft_past_the_end_of_the_file_has_no_row()
    {
        var diff = Diff("one\ntwo", "one\ntwo");

        diff.ShowDrafts([new DraftComment("a.txt", 9, "Off the end")]);

        Assert.Empty(diff.Drafts);
        Assert.DoesNotContain(Render(diff), row => row.StartsWith('┃'));
    }

    [Fact]
    public void Next_and_previous_change_step_over_draft_rows()
    {
        var diff = Diff("one\ntwo\nthree\nfour", "ONE\ntwo\nthree\nFOUR");
        diff.ShowDrafts([new DraftComment("a.txt", 1, "First."), new DraftComment("a.txt", 4, "Last.")]);

        Assert.True(diff.NextChange());

        Assert.Null(diff.CurrentDraft);
        Assert.Equal(4, diff.CurrentBufferLine + 1);
        Assert.True(diff.PreviousChange());
        Assert.Null(diff.CurrentDraft);
        Assert.Equal(1, diff.CurrentBufferLine + 1);
    }

    private DiffTab Diff(string saved, string buffer, string path = "/work/a.txt")
    {
        _fs.AddFile(path, new MockFileData(saved));
        var source = new EditorTab(_fs.FileInfo.New(path)) { Content = buffer };
        var diff = new DiffTab(source, "saved", () => DiffTab.ReadLines(source.File))
        {
            App = _app,
            Width = Columns,
            Height = 9,
        };
        diff.BeginInit();
        diff.EndInit();
        diff.Layout();
        diff.Refresh();
        return diff;
    }

    private string[] Render(DiffTab view)
    {
        var driver = _app.Driver!;
        driver.ClearContents();
        driver.Clip = new Region(driver.Screen);
        view.SetNeedsDraw();
        view.Draw();
        return Enumerable.Range(0, view.Frame.Height)
            .Select(row => string.Concat(Enumerable.Range(0, view.Frame.Width).Select(col => driver.Contents![row, col].Grapheme)))
            .ToArray();
    }
}
