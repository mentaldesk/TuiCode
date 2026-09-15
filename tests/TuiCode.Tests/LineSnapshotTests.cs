using Terminal.Gui.Drawing;
using Terminal.Gui.Views;
using TuiCode.Editor;

namespace TuiCode.Tests;

public class LineSnapshotTests
{
    [Fact]
    public void Refresh_reads_every_line_the_first_time()
    {
        var model = Model("alpha", "bravo");

        Assert.Equal(["alpha", "bravo"], new LineSnapshot().Refresh(model));
    }

    [Fact]
    public void Refresh_picks_up_a_line_edited_in_place()
    {
        var model = Model("alpha", "bravo", "charlie");
        var snapshot = new LineSnapshot();
        snapshot.Refresh(model);

        model[1][0] = model[1][0] with { Grapheme = "B" };

        Assert.Equal(["alpha", "Bravo", "charlie"], snapshot.Refresh(model));
    }

    [Fact]
    public void Refresh_picks_up_inserted_and_removed_lines()
    {
        var model = Model("alpha", "bravo", "charlie");
        var snapshot = new LineSnapshot();
        snapshot.Refresh(model);

        model.Insert(1, Cell.ToCellList("new"));
        var afterInsert = snapshot.Refresh(model).ToArray();
        model.RemoveRange(0, 2);

        Assert.Equal(["alpha", "new", "bravo", "charlie"], afterInsert);
        Assert.Equal(["bravo", "charlie"], snapshot.Refresh(model));
    }

    [Fact]
    public void Refresh_picks_up_a_line_replaced_by_another_list()
    {
        var model = Model("alpha", "bravo");
        var snapshot = new LineSnapshot();
        snapshot.Refresh(model);

        model[0] = Cell.ToCellList("ALPHA");

        Assert.Equal(["ALPHA", "bravo"], snapshot.Refresh(model));
    }

    [Fact]
    public void Refresh_matches_the_text_view_through_a_random_run_of_edits()
    {
        var random = new Random(23);
        var view = new TextView { Text = string.Join("\n", Enumerable.Range(0, 40).Select(i => $"line {i} alpha beta gamma")) };
        var snapshot = new LineSnapshot();
        snapshot.Refresh(view.GetAllLines());
        Action[] edits =
        [
            () => view.InsertText("x"),
            () => view.InsertText("p\nq\nr"),
            () => view.NewKeyDownEvent(Key.Enter),
            () => view.NewKeyDownEvent(Key.Backspace),
            () => view.NewKeyDownEvent(Key.Delete),
            () => view.InvokeCommand(Command.CutToEndOfLine),
            () => view.InvokeCommand(Command.KillWordLeft),
            () => view.InvokeCommand(Command.Undo),
            () => view.InvokeCommand(Command.Redo),
            () =>
            {
                view.SelectionStartRow = random.Next(view.Lines);
                view.SelectionStartColumn = random.Next(10);
                view.DeleteCharLeft();
            },
        ];

        for (var i = 0; i < 1000; i++)
        {
            var row = random.Next(view.Lines);
            view.InsertionPoint = new System.Drawing.Point(random.Next(view.GetLine(row).Count + 1), row);
            edits[random.Next(edits.Length)]();

            Assert.Equal(view.GetAllLines().Select(line => Cell.ToString(line)), snapshot.Refresh(view.GetAllLines()));
        }
        Assert.NotEqual(40, view.Lines);
    }

    private static List<List<Cell>> Model(params string[] lines) => [.. lines.Select(line => Cell.ToCellList(line))];
}
