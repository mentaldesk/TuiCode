using TuiCode.Abstractions;
using TuiCode.Editor;
using TuiCode.Explorer;
using TuiCode.Workbench;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Services;
using Point = System.Drawing.Point;

namespace TuiCode.Tests;

// Column select (#114).
public class EditorColumnSelectTests
{
    [Fact]
    public void Shift_arrows_select_the_same_columns_on_every_row()
    {
        var view = View("0123456789", "0123456789", "0123456789");
        view.InsertionPoint = new Point(2, 0);

        view.NewKeyDownEvent(Key.CursorRight.WithShift);
        view.NewKeyDownEvent(Key.CursorRight.WithShift);
        view.NewKeyDownEvent(Key.CursorDown.WithShift);

        Assert.Equal([Selected(1, 2, 4), Selected(0, 2, 4)], view.Carets);
    }

    [Fact]
    public void A_row_too_short_for_the_columns_clamps_without_losing_them()
    {
        var view = View("0123456789", "ab", "0123456789");
        view.InsertionPoint = new Point(5, 0);

        view.NewKeyDownEvent(Key.CursorRight.WithShift);
        view.NewKeyDownEvent(Key.CursorDown.WithShift);
        view.NewKeyDownEvent(Key.CursorDown.WithShift);

        Assert.Equal([Selected(2, 5, 6), Selected(0, 5, 6), At(1, 2)], view.Carets);
    }

    [Fact]
    public void A_box_with_no_width_leaves_a_bare_caret_on_every_row()
    {
        var view = View("abc", "abc", "abc");
        view.InsertionPoint = new Point(1, 0);

        view.NewKeyDownEvent(Key.CursorDown.WithShift);
        view.NewKeyDownEvent(Key.CursorDown.WithShift);

        Assert.Equal([At(2, 1), At(0, 1), At(1, 1)], view.Carets);
    }

    [Fact]
    public void Typing_replaces_the_block_on_every_row()
    {
        var view = View("abcd", "abcd", "abcd");
        view.InsertionPoint = new Point(1, 0);

        view.NewKeyDownEvent(Key.CursorRight.WithShift);
        view.NewKeyDownEvent(Key.CursorRight.WithShift);
        view.NewKeyDownEvent(Key.CursorDown.WithShift);
        view.NewKeyDownEvent(Key.CursorDown.WithShift);
        view.NewKeyDownEvent(Key.X);

        Assert.Equal(["axd", "axd", "axd"], view.LineStrings);
    }

    [Fact]
    public void Shrinking_the_box_back_to_its_first_row_leaves_one_selection()
    {
        var view = View("0123", "0123", "0123");
        view.InsertionPoint = new Point(1, 0);

        view.NewKeyDownEvent(Key.CursorRight.WithShift);
        view.NewKeyDownEvent(Key.CursorDown.WithShift);
        view.NewKeyDownEvent(Key.CursorDown.WithShift);
        view.NewKeyDownEvent(Key.CursorUp.WithShift);
        view.NewKeyDownEvent(Key.CursorUp.WithShift);

        Assert.Equal([Selected(0, 1, 2)], view.Carets);
        Assert.False(view.HasSecondaryCarets);
    }

    [Fact]
    public void An_arrow_key_ends_the_box_so_the_next_one_starts_where_the_caret_is()
    {
        var view = View("0123456789", "0123456789");
        view.InsertionPoint = new Point(2, 0);

        view.NewKeyDownEvent(Key.CursorRight.WithShift);
        view.NewKeyDownEvent(Key.CursorRight);
        view.NewKeyDownEvent(Key.CursorDown.WithShift);

        Assert.Equal([At(1, 4), At(0, 4)], view.Carets);
    }

    [Fact]
    public void Off_again_the_same_keys_extend_a_single_selection()
    {
        var view = View("0123456789", "0123456789");
        view.ColumnSelect = false;
        view.InsertionPoint = new Point(2, 0);

        view.NewKeyDownEvent(Key.CursorRight.WithShift);
        view.NewKeyDownEvent(Key.CursorDown.WithShift);

        Assert.Equal([new Caret(new Point(3, 1), new Point(2, 0), ColumnTrack: 3, Extending: true)], view.Carets);
    }

    [Fact]
    public void Toggling_it_on_the_group_applies_to_open_and_newly_opened_tabs()
    {
        var fs = new MockFileSystem();
        fs.AddFile("/work/a.txt", new MockFileData("a"));
        fs.AddFile("/work/b.txt", new MockFileData("b"));
        using var group = new EditorGroup();
        var first = group.OpenOrFocus(fs.FileInfo.New("/work/a.txt"));

        group.ColumnSelect = true;
        var second = group.OpenOrFocus(fs.FileInfo.New("/work/b.txt"));

        Assert.True(first.ColumnSelect);
        Assert.True(second.ColumnSelect);
    }

    private static Caret Selected(int row, int start, int end) => new(new Point(end, row), new Point(start, row), Extending: true);

    private static Caret At(int row, int column) => new(new Point(column, row));

    private static EditorTextView View(params string[] lines)
    {
        var view = new EditorTextView { Width = 80, Height = 10, ColumnSelect = true, Text = string.Join("\n", lines) };
        view.BeginInit();
        view.EndInit();
        view.Layout();
        return view;
    }
}

// Drives the chord and the keys through a TG Application — serialised (#77).
public class EditorColumnSelectHostTests : StaticConfigurationTest
{
    [Fact]
    public async Task Ctrl_T_C_turns_the_mode_on_so_shift_arrows_sweep_a_block_and_flags_it_in_the_status_bar()
    {
        var fs = new MockFileSystem();
        fs.AddFile("/work/a.txt", new MockFileData("alpha\nbravo\ncargo\n"));
        var statusBar = new StatusBarPart();
        using var workbench = new Workbench.Workbench(new SidebarPart(new FileExplorerView()), new EditorPart(), statusBar);
        var commands = new CommandService();
        using var host = new WorkbenchHost(workbench, commands, new KeybindingService(commands), new InputScopeStack(),
            new InMemorySettingsService(), driverName: DriverRegistry.Names.ANSI);
        EditorTab? tab = null;
        var whileOn = "";

        await HostSteps.Run(host,
            () =>
            {
                tab = workbench.Editor.Open(fs.FileInfo.New("/work/a.txt"));
                tab.FocusContent();
            },
            () => host.App.InjectKey(Key.T.WithCtrl),
            () => host.App.InjectKey(Key.C),
            () => { whileOn = statusBar.DisplayedText; },
            () => host.App.InjectKey(Key.CursorRight.WithShift),
            () => host.App.InjectKey(Key.CursorRight.WithShift),
            () => host.App.InjectKey(Key.CursorDown.WithShift),
            () => host.App.InjectKey(Key.CursorDown.WithShift),
            () => host.App.InjectKey(Key.X));

        Assert.Contains("Column select", whileOn);
        Assert.True(tab!.ColumnSelect);
        Assert.Equal(["xpha", "xavo", "xrgo", ""], tab.Lines);
    }
}
