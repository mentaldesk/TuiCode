using System.Drawing;
using Terminal.Gui.Input;
using TuiCode.Editor;
using TuiCode.Explorer;
using TuiCode.Workbench;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Services;

namespace TuiCode.Tests;

public class EditorTextViewLineEditTests
{
    [Fact]
    public void MoveLines_up_swaps_the_cursor_line_with_the_one_above()
    {
        var view = View("one", "two", "three");
        view.InsertionPoint = new Point(2, 1);

        view.MoveLines(LineDirection.Up);

        Assert.Equal(["two", "one", "three"], view.LineStrings);
        Assert.Equal(new Point(2, 0), view.InsertionPoint);
    }

    [Fact]
    public void MoveLines_down_swaps_the_cursor_line_with_the_one_below()
    {
        var view = View("one", "two", "three");
        view.InsertionPoint = new Point(1, 1);

        view.MoveLines(LineDirection.Down);

        Assert.Equal(["one", "three", "two"], view.LineStrings);
        Assert.Equal(new Point(1, 2), view.InsertionPoint);
    }

    [Fact]
    public void MoveLines_does_nothing_past_the_first_or_last_line()
    {
        var view = View("one", "two");
        var edits = 0;
        view.ContentsChanged += (_, _) => edits++;

        view.MoveLines(LineDirection.Up);
        view.InsertionPoint = new Point(0, 1);
        view.MoveLines(LineDirection.Down);

        Assert.Equal(["one", "two"], view.LineStrings);
        Assert.Equal(0, edits);
    }

    [Fact]
    public void MoveLines_moves_every_selected_line_and_keeps_the_selection()
    {
        var view = View("one", "two", "three", "four");
        Select(view, (1, 1), (2, 3));

        view.MoveLines(LineDirection.Down);

        Assert.Equal(["one", "four", "two", "three"], view.LineStrings);
        Assert.Equal("wo\nthr", view.SelectedText.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void MoveLines_ignores_a_selection_line_ending_at_its_start()
    {
        var view = View("one", "two", "three");
        Select(view, (2, 0), (0, 0));

        view.MoveLines(LineDirection.Down);

        Assert.Equal(["three", "one", "two"], view.LineStrings);
    }

    [Fact]
    public void MoveLines_is_undone_and_redone_in_one_step()
    {
        var view = View("one", "two", "three");
        view.InsertionPoint = new Point(0, 2);
        view.NewKeyDownEvent(Key.X);

        view.MoveLines(LineDirection.Up);
        view.MoveLines(LineDirection.Up);
        Assert.Equal(["xthree", "one", "two"], view.LineStrings);

        view.Undo();
        Assert.Equal(["one", "xthree", "two"], view.LineStrings);
        view.Undo();
        Assert.Equal(["one", "two", "xthree"], view.LineStrings);
        view.Undo();
        Assert.Equal(["one", "two", "three"], view.LineStrings);

        view.Redo();
        view.Redo();
        Assert.Equal(["one", "xthree", "two"], view.LineStrings);
        view.Redo();
        Assert.Equal(["xthree", "one", "two"], view.LineStrings);
    }

    [Fact]
    public void Typing_after_MoveLines_undoes_separately()
    {
        var view = View("one", "two");
        view.InsertionPoint = new Point(3, 1);

        view.MoveLines(LineDirection.Up);
        view.NewKeyDownEvent(Key.X);
        view.Undo();

        Assert.Equal(["two", "one"], view.LineStrings);
        view.Undo();
        Assert.Equal(["one", "two"], view.LineStrings);
    }

    [Fact]
    public void DuplicateLines_down_copies_the_line_below_and_follows_the_copy()
    {
        var view = View("one", "two", "three");
        view.InsertionPoint = new Point(2, 1);

        view.DuplicateLines(LineDirection.Down);

        Assert.Equal(["one", "two", "two", "three"], view.LineStrings);
        Assert.Equal(new Point(2, 2), view.InsertionPoint);
    }

    [Fact]
    public void DuplicateLines_up_copies_the_line_above_and_keeps_the_cursor()
    {
        var view = View("one", "two", "three");
        view.InsertionPoint = new Point(2, 1);

        view.DuplicateLines(LineDirection.Up);

        Assert.Equal(["one", "two", "two", "three"], view.LineStrings);
        Assert.Equal(new Point(2, 1), view.InsertionPoint);
    }

    [Fact]
    public void DuplicateLines_copies_every_selected_line_and_moves_the_selection_onto_the_copy()
    {
        var view = View("one", "two", "three");
        Select(view, (0, 1), (1, 2));

        view.DuplicateLines(LineDirection.Down);

        Assert.Equal(["one", "two", "one", "two", "three"], view.LineStrings);
        Assert.Equal((2, 1), (view.SelectionStartRow, view.SelectionStartColumn));
        Assert.Equal(new Point(2, 3), view.InsertionPoint);
    }

    [Fact]
    public void DuplicateLines_is_undone_and_redone_in_one_step()
    {
        var view = View("one", "two", "three");
        view.InsertionPoint = new Point(0, 2);
        view.NewKeyDownEvent(Key.X);
        Select(view, (0, 0), (1, 1));

        view.DuplicateLines(LineDirection.Down);
        view.Undo();

        Assert.Equal(["one", "two", "xthree"], view.LineStrings);
        view.Undo();
        Assert.Equal(["one", "two", "three"], view.LineStrings);

        view.Redo();
        view.Redo();
        Assert.Equal(["one", "two", "one", "two", "xthree"], view.LineStrings);
    }

    [Fact]
    public void Line_edits_raise_ContentsChanged_once()
    {
        var view = View("one", "two");
        var edits = 0;
        view.ContentsChanged += (_, _) => edits++;

        view.MoveLines(LineDirection.Down);
        view.DuplicateLines(LineDirection.Up);

        Assert.Equal(2, edits);
    }

    private static void Select(EditorTextView view, (int Row, int Column) anchor, (int Row, int Column) cursor)
    {
        view.InsertionPoint = new Point(cursor.Column, cursor.Row);
        view.SelectionStartRow = anchor.Row;
        view.SelectionStartColumn = anchor.Column;
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

// Boots a TG Application — serialised (#77).
public class LineEditHostTests : StaticConfigurationTest
{
    [Fact]
    public async Task Alt_arrows_move_and_Alt_Shift_arrows_duplicate_the_cursor_line()
    {
        var fs = new MockFileSystem();
        fs.AddFile("/work/a.txt", new MockFileData("one\ntwo\nthree"));
        using var workbench = new TuiCode.Workbench.Workbench(new SidebarPart(new FileExplorerView()), new EditorPart(), new StatusBarPart());
        var commands = new CommandService();
        using var host = new WorkbenchHost(workbench, commands, new KeybindingService(commands), new InputScopeStack(),
            new InMemorySettingsService(), driverName: DriverRegistry.Names.ANSI);
        EditorTab? tab = null;
        IReadOnlyList<string> moved = [];

        await HostSteps.Run(host,
            () =>
            {
                tab = workbench.Editor.Open(fs.FileInfo.New("/work/a.txt"));
                tab.MoveCursor(1, 0);
            },
            () => host.App.InjectKey(Key.CursorUp.WithAlt),
            () => { moved = tab!.Lines; },
            () => host.App.InjectKey(Key.CursorDown.WithAlt.WithShift));

        Assert.Equal(["two", "one", "three"], moved);
        Assert.Equal(["two", "two", "one", "three"], tab!.Lines);
        Assert.Equal(1, tab.CursorRow);
    }
}

// Boots a TG Application — serialised (#77).
public class MultipleCursorHostTests : StaticConfigurationTest
{
    [Fact]
    public async Task Ctrl_Alt_Down_adds_cursors_that_type_together_and_Esc_removes_them()
    {
        var fs = new MockFileSystem();
        fs.AddFile("/work/a.txt", new MockFileData("one\ntwo\nthree"));
        using var workbench = new TuiCode.Workbench.Workbench(new SidebarPart(new FileExplorerView()), new EditorPart(), new StatusBarPart());
        var commands = new CommandService();
        using var host = new WorkbenchHost(workbench, commands, new KeybindingService(commands), new InputScopeStack(),
            new InMemorySettingsService(), driverName: DriverRegistry.Names.ANSI);
        EditorTab? tab = null;
        var hadCursors = false;

        await HostSteps.Run(host,
            () =>
            {
                tab = workbench.Editor.Open(fs.FileInfo.New("/work/a.txt"));
                tab.MoveCursor(0, 0);
            },
            () => host.App.InjectKey(Key.CursorDown.WithCtrl.WithAlt),
            () => host.App.InjectKey(Key.CursorDown.WithCtrl.WithAlt),
            () => host.App.InjectKey(Key.X),
            () => { hadCursors = tab!.HasSecondaryCursors; },
            () => host.App.InjectKey(Key.Esc),
            () => host.App.InjectKey(Key.Y));

        Assert.True(hadCursors);
        Assert.Equal(["xyone", "xtwo", "xthree"], tab!.Lines);
        Assert.False(tab.HasSecondaryCursors);
    }
}
