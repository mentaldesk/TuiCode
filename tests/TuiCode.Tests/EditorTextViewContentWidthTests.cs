using Terminal.Gui.Drawing;
using Terminal.Gui.Views;
using TuiCode.Editor;

namespace TuiCode.Tests;

public class EditorTextViewContentWidthTests
{
    // A double-width character jumps past the widest width without first tying it.
    [Fact]
    public void Content_width_grows_when_a_line_is_typed_past_the_widest()
    {
        var view = View("a wide line", "a wider line");
        view.InsertionPoint = new System.Drawing.Point(11, 0);

        view.InsertText("世");
        view.Layout();

        Assert.Equal(11 + 2 + 1, view.GetContentSize().Width);
    }

    // Ctrl+K, unlike Backspace, has no width check of TG's own to fall back on.
    [Fact]
    public void Content_width_shrinks_when_the_widest_line_is_shortened()
    {
        var view = View("a wider line", "short");
        view.InsertionPoint = new System.Drawing.Point(2, 0);

        view.NewKeyDownEvent(Key.K.WithCtrl);
        view.SetNeedsLayout();
        view.Layout();

        Assert.Equal("short".Length + 1, view.GetContentSize().Width);
    }

    [Fact]
    public void Content_width_shrinks_only_once_every_line_at_the_widest_is_shortened()
    {
        var view = View("widest", "short", "widest");

        view.InsertionPoint = new System.Drawing.Point(6, 0);
        view.NewKeyDownEvent(Key.Backspace);
        view.Layout();
        var afterFirst = view.GetContentSize().Width;
        view.InsertionPoint = new System.Drawing.Point(6, 2);
        view.NewKeyDownEvent(Key.Backspace);
        view.Layout();

        Assert.Equal("widest".Length + 1, afterFirst);
        Assert.Equal("widest".Length, view.GetContentSize().Width);
    }

    [Fact]
    public void Viewport_scrolls_to_keep_the_cursor_visible_when_a_line_grows_past_the_widest()
    {
        var view = View("short", "a wider line");
        view.Width = 10;
        view.Layout();
        view.InsertionPoint = new System.Drawing.Point(5, 0);

        for (var i = 0; i < 15; i++) view.InsertText("世");
        view.Layout();

        var cursor = Width(view.GetLine(0)[..view.CurrentColumn]);
        Assert.InRange(cursor - view.Viewport.X, 0, view.Viewport.Width - 1);
    }

    [Fact]
    public void Content_width_matches_the_widest_line_through_a_random_run_of_edits()
    {
        var random = new Random(17);
        var view = View([.. Enumerable.Range(0, 30).Select(_ => RandomLine(random))]);
        Action[] edits =
        [
            () => view.NewKeyDownEvent(Key.Z),
            () => view.NewKeyDownEvent(Key.Space),
            () => view.InsertText("世"),
            () => view.NewKeyDownEvent(Key.Enter),
            () => view.NewKeyDownEvent(Key.Backspace),
            () => view.NewKeyDownEvent(Key.Delete),
            () => view.NewKeyDownEvent(Key.K.WithCtrl),
            () => view.NewKeyDownEvent(Key.Backspace.WithCtrl),
            () => view.NewKeyDownEvent(Key.Delete.WithCtrl),
            () => view.NewKeyDownEvent(Key.Backspace.WithCtrl.WithShift),
            () => view.InsertText(RandomLine(random)),
            () => view.InsertText($"{RandomLine(random)}\n{RandomLine(random)}"),
            () => view.InvokeCommand(Command.Undo),
            () => view.InvokeCommand(Command.Redo),
            () =>
            {
                view.SelectionStartRow = random.Next(view.Lines);
                view.SelectionStartColumn = random.Next(view.GetLine(view.SelectionStartRow).Count + 1);
                if (random.Next(2) == 0) view.DeleteCharLeft();
                else view.NewKeyDownEvent(Key.Z);
            },
        ];

        for (var i = 0; i < 2000; i++)
        {
            var row = random.Next(3) == 0 ? WidestRow(view) : random.Next(view.Lines);
            view.InsertionPoint = new System.Drawing.Point(random.Next(view.GetLine(row).Count + 1), row);
            edits[random.Next(edits.Length)]();
            // Kill commands leave the content size stale until the next layout, with or without the cache.
            view.SetNeedsLayout();
            view.Layout();

            Assert.Equal(view.GetAllLines().Max(Width) + 1, view.GetContentSize().Width);
        }
    }

    private static string RandomLine(Random random) =>
        new([.. Enumerable.Range(0, random.Next(25)).Select(_ => random.Next(4) == 0 ? ' ' : 'a')]);

    private static int Width(List<Cell> line) => line.Sum(cell => cell.Grapheme == "世" ? 2 : 1);

    private static int WidestRow(TextView view)
    {
        var lines = view.GetAllLines();
        return lines.IndexOf(lines.MaxBy(Width)!);
    }

    private static EditorTextView View(params string[] lines)
    {
        var view = new EditorTextView { Width = 80, Height = 10, Text = string.Join("\n", lines) };
        view.BeginInit();
        view.EndInit();
        view.Layout();
        return view;
    }
}
