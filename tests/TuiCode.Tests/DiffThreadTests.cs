using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;
using TuiCode.Abstractions;
using TuiCode.Editor;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace TuiCode.Tests;

// Review threads in the diff (#186). Renders through a TG driver — serialised (#77).
public class DiffThreadTests : StaticConfigurationTest
{
    private static readonly DateTimeOffset Posted = new(2026, 9, 20, 6, 54, 0, TimeSpan.Zero);

    private const int Columns = 31;

    private readonly IApplication _app = Application.Create().Init(DriverRegistry.Names.ANSI);
    private readonly MockFileSystem _fs = new();

    public DiffThreadTests() => _app.Driver!.SetScreenSize(40, 10);

    public override void Dispose()
    {
        _app.Dispose();
        base.Dispose();
    }

    [Fact]
    public void A_thread_sits_under_its_line_across_both_panes()
    {
        var diff = Diff("one\ntwo\nthree", "one\nTWO\nthree");

        diff.ShowThreads([Thread(2, "octocat", "Does Esc stay global here?", replies: 2)]);

        Assert.Equal(
        [
            " saved         │ working copy  ",
            "  1  one       │  1  one       ",
            "  2- two       │  2+ TWO       ",
            "┃ octocat: Does Esc (2 replies)",
            "  3  three     │  3  three     ",
        ], Render(diff)[..5]);
    }

    [Fact]
    public void A_thread_whose_line_is_gone_is_left_to_the_Review_tab()
    {
        var diff = Diff("one\ntwo", "one\ntwo");

        diff.ShowThreads([Thread(2, "octocat", "Still here?", outdated: true), Thread(9, "octocat", "Off the end")]);

        Assert.Empty(diff.Threads);
        Assert.DoesNotContain(Render(diff), row => row.StartsWith('┃'));
    }

    [Fact]
    public void A_resolved_thread_is_drawn_faint_and_an_unresolved_one_is_not()
    {
        var diff = Diff("one\ntwo", "one\nTWO");
        diff.ShowThreads([Thread(2, "octocat", "Settled.", resolved: true)]);

        Render(diff);

        Assert.True(AttributeAt(3, 0).Style.HasFlag(TextStyle.Faint));
        diff.ShowThreads([Thread(2, "octocat", "Open.")]);
        Render(diff);
        Assert.False(AttributeAt(3, 0).Style.HasFlag(TextStyle.Faint));
    }

    [Fact]
    public void A_thread_row_is_drawn_on_a_background_of_its_own()
    {
        var diff = Diff("one\ntwo", "one\nTWO");
        diff.ShowThreads([Thread(2, "octocat", "Look here.")]);

        Render(diff);

        var comment = AttributeAt(3, 0).Background;
        Assert.DoesNotContain(comment, BackgroundsOf(1));
        Assert.DoesNotContain(comment, BackgroundsOf(2));
    }

    [Fact]
    public void Down_moves_onto_the_thread_row_and_Enter_expands_and_collapses_it()
    {
        var diff = Diff("one\ntwo", "one\nTWO");
        diff.ShowThreads([Thread(2, "octocat", "Does Esc stay global here?", replies: 1)]);

        diff.NewKeyDownEvent(Key.CursorDown);
        diff.NewKeyDownEvent(Key.CursorDown);

        Assert.NotNull(diff.CurrentThread);
        Assert.True(diff.ToggleThread());
        Assert.Equal(
        [
            "┃ octocat · 2026-09-20 06:54   ",
            "┃ Does Esc stay global here?   ",
            "┃                              ",
            "┃ hubot · 2026-09-20 06:54     ",
            "┃ Reply 1                      ",
        ], Render(diff)[3..8]);

        Assert.True(diff.ToggleThread());
        Assert.Equal("┃ octocat: Does Esc s (1 reply)", Render(diff)[3].TrimEnd());
        Assert.NotNull(diff.CurrentThread);
    }

    [Fact]
    public void Enter_on_a_row_of_the_diff_is_nothing_to_do_with_threads()
    {
        var diff = Diff("one\ntwo", "one\nTWO");
        diff.ShowThreads([Thread(2, "octocat", "Look here.")]);

        Assert.False(diff.ToggleThread());
        Assert.Null(diff.CurrentThread);
    }

    [Fact]
    public void Next_and_previous_change_step_over_thread_rows()
    {
        var diff = Diff("one\ntwo\nthree\nfour", "ONE\ntwo\nthree\nFOUR");
        diff.ShowThreads([Thread(1, "octocat", "First."), Thread(4, "octocat", "Last.")]);

        Assert.True(diff.NextChange());

        Assert.Null(diff.CurrentThread);
        Assert.Equal(4, diff.CurrentBufferLine + 1);
        Assert.True(diff.PreviousChange());
        Assert.Null(diff.CurrentThread);
        Assert.Equal(1, diff.CurrentBufferLine + 1);
    }

    [Fact]
    public void An_expanded_thread_stays_expanded_when_the_diff_is_recomputed()
    {
        var diff = Diff("one\ntwo", "one\nTWO");
        var thread = Thread(2, "octocat", "Does Esc stay global here?", replies: 1);
        diff.ShowThreads([thread]);
        diff.NewKeyDownEvent(Key.CursorDown);
        diff.NewKeyDownEvent(Key.CursorDown);
        diff.ToggleThread();

        diff.Refresh();

        Assert.Equal("┃ octocat · 2026-09-20 06:54   ", Render(diff)[3]);
    }

    private static GitHubReviewThread Thread(int line, string author, string body, int replies = 0, bool resolved = false, bool outdated = false) =>
        new("a.txt", outdated ? null : line, resolved, outdated,
        [
            new GitHubComment(author, Posted, body),
            .. Enumerable.Range(1, replies).Select(i => new GitHubComment("hubot", Posted, $"Reply {i}")),
        ]);

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

    private Attribute AttributeAt(int row, int col) => _app.Driver!.Contents![row, col].Attribute!.Value;

    private IEnumerable<Color> BackgroundsOf(int row) =>
        Enumerable.Range(0, Columns).Select(col => AttributeAt(row, col).Background);

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
