using Terminal.Gui.Input;
using TuiCode.Editor;
using Point = System.Drawing.Point;

namespace TuiCode.Tests;

public class EditorAutoIndentTests
{
    [Fact]
    public void Enter_at_the_end_of_an_indented_line_starts_the_next_line_at_the_same_indent()
    {
        var view = View("    foo");
        view.InsertionPoint = new Point(7, 0);

        view.NewKeyDownEvent(Key.Enter);

        Assert.Equal(["    foo", "    "], view.LineStrings);
        Assert.Equal(new Point(4, 1), view.InsertionPoint);
    }

    [Theory]
    [InlineData("\tfoo", "\t")]
    [InlineData("\t  foo", "\t  ")]
    [InlineData("  \t foo", "  \t ")]
    public void Enter_carries_tabs_and_spaces_over_verbatim(string line, string indent)
    {
        var view = View(line);
        view.InsertionPoint = new Point(line.Length, 0);

        view.NewKeyDownEvent(Key.Enter);

        Assert.Equal([line, indent], view.LineStrings);
        Assert.Equal(new Point(indent.Length, 1), view.InsertionPoint);
    }

    [Fact]
    public void Enter_inside_the_indent_carries_only_the_whitespace_left_of_the_caret()
    {
        var view = View("    foo");
        view.InsertionPoint = new Point(2, 0);

        view.NewKeyDownEvent(Key.Enter);

        Assert.Equal(["  ", "    foo"], view.LineStrings);
        Assert.Equal(new Point(2, 1), view.InsertionPoint);
    }

    [Fact]
    public void Enter_at_the_start_of_an_indented_line_moves_it_down_unchanged()
    {
        var view = View("    foo");

        view.NewKeyDownEvent(Key.Enter);

        Assert.Equal(["", "    foo"], view.LineStrings);
        Assert.Equal(new Point(0, 1), view.InsertionPoint);
    }

    [Fact]
    public void Enter_mid_line_moves_the_rest_behind_the_indent_dropping_whitespace_before_it()
    {
        var view = View("    foo(  bar)");
        view.InsertionPoint = new Point(8, 0);

        view.NewKeyDownEvent(Key.Enter);

        Assert.Equal(["    foo(", "    bar)"], view.LineStrings);
        Assert.Equal(new Point(4, 1), view.InsertionPoint);
    }

    [Fact]
    public void Enter_on_an_unindented_line_starts_the_next_line_at_column_zero()
    {
        var view = View("foo", "bar");
        view.InsertionPoint = new Point(3, 1);

        view.NewKeyDownEvent(Key.Enter);

        Assert.Equal(["foo", "bar", ""], view.LineStrings);
        Assert.Equal(new Point(0, 2), view.InsertionPoint);
    }

    [Fact]
    public void Enter_on_the_last_line_keeps_its_indent()
    {
        var view = View("foo", "  bar");
        view.InsertionPoint = new Point(5, 1);

        view.NewKeyDownEvent(Key.Enter);

        Assert.Equal(["foo", "  bar", "  "], view.LineStrings);
    }

    [Fact]
    public void Enter_with_a_selection_replaces_it_then_keeps_the_indent()
    {
        var view = View("    foo bar");
        view.SetCarets([new Caret(new Point(11, 0), new Point(7, 0), Extending: true)]);

        view.NewKeyDownEvent(Key.Enter);

        Assert.Equal(["    foo", "    "], view.LineStrings);
        Assert.Equal(new Point(4, 1), view.InsertionPoint);
    }

    [Fact]
    public void Enter_twice_leaves_the_line_between_empty()
    {
        var view = View("    foo");
        view.InsertionPoint = new Point(7, 0);

        view.NewKeyDownEvent(Key.Enter);
        view.NewKeyDownEvent(Key.Enter);

        Assert.Equal(["    foo", "", "    "], view.LineStrings);
        Assert.Equal(new Point(4, 2), view.InsertionPoint);
    }

    [Fact]
    public void Arrows_off_an_auto_indented_line_trim_it()
    {
        var view = View("    foo", "bar");
        view.InsertionPoint = new Point(7, 0);
        view.NewKeyDownEvent(Key.Enter);

        view.NewKeyDownEvent(Key.CursorDown);

        Assert.Equal(["    foo", "", "bar"], view.LineStrings);
    }

    [Fact]
    public void Moving_along_an_auto_indented_line_keeps_its_whitespace()
    {
        var view = View("    foo");
        view.InsertionPoint = new Point(7, 0);
        view.NewKeyDownEvent(Key.Enter);

        view.NewKeyDownEvent(Key.CursorLeft);

        Assert.Equal(["    foo", "    "], view.LineStrings);
    }

    [Fact]
    public void A_click_off_an_auto_indented_line_trims_it()
    {
        var view = View("    foo", "bar");
        view.InsertionPoint = new Point(7, 0);
        view.NewKeyDownEvent(Key.Enter);

        view.NewMouseEvent(new Mouse { Flags = MouseFlags.LeftButtonPressed, Position = new Point(1, 0) });

        Assert.Equal(["    foo", "", "bar"], view.LineStrings);
    }

    [Fact]
    public void Go_to_line_off_an_auto_indented_line_trims_it()
    {
        using var tab = OpenTab("    foo\nbar");
        tab.MoveCursor(0, 7);
        tab.TextView.NewKeyDownEvent(Key.Enter);

        tab.MoveCursor(2, 0);

        Assert.Equal(["    foo", "", "bar"], tab.TextView.LineStrings);
    }

    [Fact]
    public void A_line_that_gained_text_is_kept()
    {
        var view = View("    foo", "bar");
        view.InsertionPoint = new Point(7, 0);
        view.NewKeyDownEvent(Key.Enter);

        view.NewKeyDownEvent(Key.X);
        view.NewKeyDownEvent(Key.CursorDown);

        Assert.Equal(["    foo", "    x", "bar"], view.LineStrings);
    }

    [Fact]
    public void Whitespace_typed_onto_a_blank_line_is_kept()
    {
        var view = View("", "bar");

        view.NewKeyDownEvent(Key.Space);
        view.NewKeyDownEvent(Key.Space);
        view.NewKeyDownEvent(Key.CursorDown);

        Assert.Equal(["  ", "bar"], view.LineStrings);
    }

    [Fact]
    public void Whitespace_typed_after_the_auto_indent_is_kept()
    {
        var view = View("    foo", "bar");
        view.InsertionPoint = new Point(7, 0);
        view.NewKeyDownEvent(Key.Enter);

        view.NewKeyDownEvent(Key.Space);
        view.NewKeyDownEvent(Key.CursorDown);

        Assert.Equal(["    foo", "     ", "bar"], view.LineStrings);
    }

    [Fact]
    public void Each_caret_keeps_its_own_lines_indent()
    {
        var view = View("  a", "\tb", "c");
        view.SetCarets([At(0, 3), At(1, 2), At(2, 1)]);

        view.NewKeyDownEvent(Key.Enter);

        Assert.Equal(["  a", "  ", "\tb", "\t", "c", ""], view.LineStrings);
        Assert.Equal([At(1, 2), At(3, 1), At(5, 0)], view.Carets);
    }

    [Fact]
    public void Enter_twice_at_several_carets_leaves_each_line_between_empty()
    {
        var view = View("  a", "    b");
        view.SetCarets([At(0, 3), At(1, 5)]);

        view.NewKeyDownEvent(Key.Enter);
        view.NewKeyDownEvent(Key.Enter);

        Assert.Equal(["  a", "", "  ", "    b", "", "    "], view.LineStrings);
    }

    [Fact]
    public void One_undo_takes_back_an_Enter_at_several_carets_with_its_indent()
    {
        var view = View("  a", "    b");
        view.SetCarets([At(0, 3), At(1, 5)]);

        view.NewKeyDownEvent(Key.Enter);
        view.Undo();

        Assert.Equal(["  a", "    b"], view.LineStrings);
        Assert.Equal([At(0, 3), At(1, 5)], view.Carets);
    }

    [Fact]
    public void Undoing_the_second_Enter_puts_back_the_indent_it_trimmed()
    {
        var view = View("    foo");
        view.InsertionPoint = new Point(7, 0);
        view.NewKeyDownEvent(Key.Enter);
        view.NewKeyDownEvent(Key.Enter);

        view.Undo();
        Assert.Equal(["    foo", "    "], view.LineStrings);
        Assert.Equal(new Point(4, 1), view.InsertionPoint);

        view.Undo();
        Assert.Equal(["    foo"], view.LineStrings);
    }

    [Fact]
    public void A_trim_on_moving_away_is_not_an_undo_step_of_its_own()
    {
        var view = View("    foo", "bar");
        view.InsertionPoint = new Point(7, 0);
        view.NewKeyDownEvent(Key.Enter);
        view.NewKeyDownEvent(Key.CursorDown);

        view.Undo();

        Assert.Equal(["    foo", "bar"], view.LineStrings);
    }

    [Fact]
    public void Saving_on_an_auto_indented_line_keeps_its_whitespace()
    {
        var fs = new MockFileSystem();
        fs.AddFile("/work/file.txt", new MockFileData("    foo"));
        using var tab = new EditorTab(fs.FileInfo.New("/work/file.txt"));
        tab.MoveCursor(0, 7);
        tab.TextView.NewKeyDownEvent(Key.Enter);

        tab.Save();

        Assert.Equal("    foo\n    \n", fs.File.ReadAllText("/work/file.txt"));
        Assert.Equal(["    foo", "    "], tab.TextView.LineStrings);
    }

    private static Caret At(int row, int column) => new(new Point(column, row));

    private static EditorTab OpenTab(string content)
    {
        var fs = new MockFileSystem();
        fs.AddFile("/work/file.txt", new MockFileData(content));
        return new EditorTab(fs.FileInfo.New("/work/file.txt"));
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
