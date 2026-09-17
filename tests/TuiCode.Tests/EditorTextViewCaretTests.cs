using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Input;
using TuiCode.Editor;
using Point = System.Drawing.Point;

namespace TuiCode.Tests;

public class EditorTextViewCaretTests
{
    [Fact]
    public void Typing_inserts_at_every_caret()
    {
        var view = View("abc", "abc", "abc");
        view.SetCarets([At(0, 1), At(1, 1), At(2, 3)]);

        view.NewKeyDownEvent(Key.X);

        Assert.Equal(["axbc", "axbc", "abcx"], view.LineStrings);
        Assert.Equal([At(0, 2), At(1, 2), At(2, 4)], view.Carets);
    }

    [Fact]
    public void Typing_at_two_carets_on_one_line_keeps_the_later_caret_after_the_earlier_insert()
    {
        var view = View("abcd");
        view.SetCarets([At(0, 0), At(0, 2)]);

        view.NewKeyDownEvent(Key.X);
        view.NewKeyDownEvent(Key.Y);

        Assert.Equal(["xyabxycd"], view.LineStrings);
        Assert.Equal([At(0, 2), At(0, 6)], view.Carets);
    }

    [Fact]
    public void Enter_splits_the_line_at_every_caret()
    {
        var view = View("ab", "cd");
        view.SetCarets([At(0, 1), At(1, 1)]);

        view.NewKeyDownEvent(Key.Enter);

        Assert.Equal(["a", "b", "c", "d"], view.LineStrings);
        Assert.Equal([At(1, 0), At(3, 0)], view.Carets);
    }

    [Fact]
    public void Carets_that_meet_after_an_edit_merge()
    {
        var view = View("ab", "cd");
        view.SetCarets([At(1, 0), At(1, 1)]);

        view.NewKeyDownEvent(Key.Backspace);

        Assert.Equal(["abd"], view.LineStrings);
        Assert.Equal([At(0, 2)], view.Carets);
        Assert.False(view.HasSecondaryCarets);
    }

    [Fact]
    public void Arrows_move_every_caret()
    {
        var view = View("abc", "a", "abc");
        view.SetCarets([At(0, 2), At(2, 1)]);

        view.NewKeyDownEvent(Key.CursorDown);
        view.NewKeyDownEvent(Key.CursorLeft);

        // Down on the last line goes to its end.
        Assert.Equal([At(1, 0), At(2, 2)], view.Carets.Select(c => c with { ColumnTrack = -1 }));
    }

    [Fact]
    public void Shift_arrows_extend_a_selection_at_every_caret_and_typing_replaces_them()
    {
        var view = View("one two", "one two");
        view.SetCarets([At(0, 0), At(1, 4)]);

        view.NewKeyDownEvent(Key.CursorRight.WithShift);
        view.NewKeyDownEvent(Key.CursorRight.WithShift);
        view.NewKeyDownEvent(Key.CursorRight.WithShift);
        Assert.All(view.Carets, caret => Assert.NotNull(caret.Anchor));
        view.NewKeyDownEvent(Key.X);

        Assert.Equal(["x two", "one x"], view.LineStrings);
        Assert.Equal([At(0, 1), At(1, 5)], view.Carets);
    }

    [Fact]
    public void Kill_to_end_of_line_applies_at_every_caret()
    {
        var view = View("keep drop", "keep drop");
        view.SetCarets([At(0, 4), At(1, 4)]);

        view.NewKeyDownEvent(Key.K.WithCtrl);

        Assert.Equal(["keep", "keep"], view.LineStrings);
    }

    [Fact]
    public void A_multi_caret_edit_is_undone_and_redone_in_one_step_with_its_carets()
    {
        var view = View("abc", "abc");
        view.InsertionPoint = new Point(3, 0);
        view.NewKeyDownEvent(Key.Z);
        view.SetCarets([At(0, 1), At(1, 1)]);

        view.NewKeyDownEvent(Key.X);
        view.NewKeyDownEvent(Key.Enter);
        Assert.Equal(["ax", "bcz", "ax", "bc"], view.LineStrings);

        view.Undo();
        Assert.Equal(["axbcz", "axbc"], view.LineStrings);
        Assert.Equal([At(0, 2), At(1, 2)], view.Carets);
        view.Undo();
        Assert.Equal(["abcz", "abc"], view.LineStrings);
        Assert.Equal([At(0, 1), At(1, 1)], view.Carets);
        view.Undo();
        Assert.Equal(["abc", "abc"], view.LineStrings);
        Assert.False(view.HasSecondaryCarets);

        view.Redo();
        view.Redo();
        view.Redo();
        Assert.Equal(["ax", "bcz", "ax", "bc"], view.LineStrings);
        Assert.Equal([At(1, 0), At(3, 0)], view.Carets);
    }

    // TG 2.1.0's own undo left these wrong.
    [Fact]
    public void Undo_restores_a_line_cut_with_ctrl_K()
    {
        var view = View("keep drop");
        view.InsertionPoint = new Point(4, 0);

        view.NewKeyDownEvent(Key.K.WithCtrl);
        view.Undo();

        Assert.Equal(["keep drop"], view.LineStrings);
    }

    [Fact]
    public void Undo_restores_text_typed_over_a_multi_line_selection_and_the_selection()
    {
        var view = View("one", "two", "three");
        var selection = new Caret(new Point(2, 2), new Point(1, 0), Extending: true);
        view.SetCarets([selection]);

        view.NewKeyDownEvent(Key.X);
        Assert.Equal(["oxree"], view.LineStrings);
        view.Undo();

        Assert.Equal(["one", "two", "three"], view.LineStrings);
        Assert.Equal([selection], view.Carets);
    }

    [Fact]
    public void A_multi_caret_edit_raises_ContentsChanged_once()
    {
        var view = View("a", "b", "c");
        view.SetCarets([At(0, 0), At(1, 0), At(2, 0)]);
        var edits = 0;
        view.ContentsChanged += (_, _) => edits++;

        view.NewKeyDownEvent(Key.X);

        Assert.Equal(1, edits);
    }

    [Fact]
    public void Commands_that_act_on_the_whole_buffer_collapse_to_the_primary_caret()
    {
        var view = View("abc", "abc");
        view.SetCarets([At(1, 1), At(0, 1)]);

        view.NewKeyDownEvent(Key.A.WithCtrl);

        Assert.False(view.HasSecondaryCarets);
    }

    [Fact]
    public void AddCaret_below_keeps_the_column_across_a_shorter_line()
    {
        var view = View("abcdef", "ab", "abcdef");
        view.InsertionPoint = new Point(4, 0);

        view.AddCaret(LineDirection.Down);
        Assert.Equal([new Point(4, 0), new Point(2, 1)], view.Carets.Select(c => c.Position));
        view.AddCaret(LineDirection.Down);

        Assert.Equal([new Point(4, 0), new Point(2, 1), new Point(4, 2)], view.Carets.Select(c => c.Position));
    }

    [Fact]
    public void AddCaret_above_stops_at_the_first_line()
    {
        var view = View("ab", "ab");
        view.InsertionPoint = new Point(1, 1);

        view.AddCaret(LineDirection.Up);
        view.AddCaret(LineDirection.Up);

        Assert.Equal([new Point(1, 1), new Point(1, 0)], view.Carets.Select(c => c.Position));
    }

    [Fact]
    public void Alt_click_adds_a_caret_and_removes_it_again()
    {
        var view = View("abc", "abc");
        view.InsertionPoint = new Point(0, 0);

        view.NewMouseEvent(AltClick(2, 1));
        Assert.Equal([At(0, 0), At(1, 2)], view.Carets);

        view.NewMouseEvent(AltClick(2, 1));
        Assert.Equal([At(0, 0)], view.Carets);
    }

    [Fact]
    public void Alt_click_on_the_primary_caret_promotes_the_next()
    {
        var view = View("abc", "abc");
        view.SetCarets([At(0, 0), At(1, 2)]);

        view.NewMouseEvent(AltClick(0, 0));

        Assert.Equal([At(1, 2)], view.Carets);
    }

    [Fact]
    public void A_plain_click_removes_the_other_carets()
    {
        var view = View("abc", "abc");
        view.SetCarets([At(0, 0), At(1, 2)]);

        view.NewMouseEvent(new Mouse { Flags = MouseFlags.LeftButtonClicked, Position = new Point(1, 0) });

        Assert.False(view.HasSecondaryCarets);
    }

    [Fact]
    public void MoveLines_moves_the_lines_of_every_caret()
    {
        var view = View("a", "b", "c", "d", "e");
        view.SetCarets([At(1, 0), At(3, 0)]);

        view.MoveLines(LineDirection.Up);

        Assert.Equal(["b", "a", "d", "c", "e"], view.LineStrings);
        Assert.Equal([At(0, 0), At(2, 0)], view.Carets);
    }

    [Fact]
    public void MoveLines_moves_adjacent_carets_lines_as_one_block()
    {
        var view = View("a", "b", "c", "d");
        view.SetCarets([At(1, 0), At(2, 0)]);

        view.MoveLines(LineDirection.Down);

        Assert.Equal(["a", "d", "b", "c"], view.LineStrings);
        Assert.Equal([At(2, 0), At(3, 0)], view.Carets);
    }

    [Fact]
    public void DuplicateLines_duplicates_the_lines_of_every_caret()
    {
        var view = View("a", "b", "c");
        view.SetCarets([At(0, 0), At(2, 0)]);

        view.DuplicateLines(LineDirection.Down);

        Assert.Equal(["a", "a", "b", "c", "c"], view.LineStrings);
        Assert.Equal([At(1, 0), At(4, 0)], view.Carets);
    }

    private static Caret At(int row, int column) => new(new Point(column, row));

    private static Mouse AltClick(int column, int row) =>
        new() { Flags = MouseFlags.LeftButtonClicked | MouseFlags.Alt, Position = new Point(column, row) };

    private static EditorTextView View(params string[] lines)
    {
        var view = new EditorTextView { Width = 80, Height = 10, Text = string.Join("\n", lines) };
        view.BeginInit();
        view.EndInit();
        view.Layout();
        return view;
    }
}

// Uses a TG Application for its clipboard and driver — serialised (#77).
public class EditorTextViewCaretAppTests : StaticConfigurationTest
{
    private readonly IApplication _app = Application.Create().Init(DriverRegistry.Names.ANSI);

    public EditorTextViewCaretAppTests() => _app.Driver!.SetScreenSize(40, 10);

    public override void Dispose()
    {
        _app.Dispose();
        base.Dispose();
    }

    [Fact]
    public void Copy_joins_each_carets_selection_and_paste_puts_one_back_at_each()
    {
        var view = View("one 1", "two 2");
        view.SetCarets([Selecting(0, 0, 3), Selecting(1, 0, 3)]);

        view.NewKeyDownEvent(Key.C.WithCtrl);
        Assert.Equal($"one{Environment.NewLine}two", _app.Clipboard!.GetClipboardData());
        view.SetCarets([At(0, 5), At(1, 5)]);
        view.NewKeyDownEvent(Key.V.WithCtrl);

        Assert.Equal(["one 1one", "two 2two"], view.LineStrings);
    }

    [Fact]
    public void Paste_puts_the_whole_clipboard_at_each_caret_when_its_lines_do_not_match_the_carets()
    {
        var view = View("a", "b");
        _app.Clipboard!.SetClipboardData("xy");
        view.SetCarets([At(0, 1), At(1, 1)]);

        view.NewKeyDownEvent(Key.V.WithCtrl);

        Assert.Equal(["axy", "bxy"], view.LineStrings);
        Assert.Equal([At(0, 3), At(1, 3)], view.Carets);
    }

    [Fact]
    public void Cut_with_no_selection_removes_each_carets_line()
    {
        var view = View("a", "b", "c");
        view.SetCarets([At(0, 0), At(2, 0)]);

        view.NewKeyDownEvent(Key.X.WithCtrl);

        Assert.Equal(["b"], view.LineStrings);
        Assert.Equal($"a{Environment.NewLine}c", _app.Clipboard!.GetClipboardData());
    }

    [Fact]
    public void Secondary_carets_are_drawn_in_reverse_video_with_their_selections()
    {
        var view = View("abcdef", "abcdef");
        view.SetCarets([At(0, 0), Selecting(1, 1, 3)]);

        view.SetNeedsDraw();
        view.Draw();

        var contents = _app.Driver!.Contents!;
        Assert.True(contents[1, 3].Attribute!.Value.Style.HasFlag(TextStyle.Reverse));
        Assert.Equal(view.GetAttributeForRole(VisualRole.Active), contents[1, 1].Attribute);
        Assert.Equal(view.GetAttributeForRole(VisualRole.Editable), contents[1, 4].Attribute);
    }

    private static Caret At(int row, int column) => new(new Point(column, row));

    private static Caret Selecting(int row, int from, int to) => new(new Point(to, row), new Point(from, row), Extending: true);

    private EditorTextView View(params string[] lines)
    {
        var view = new EditorTextView { App = _app, Width = 40, Height = 10, Text = string.Join("\n", lines) };
        view.BeginInit();
        view.EndInit();
        view.Layout();
        return view;
    }
}
