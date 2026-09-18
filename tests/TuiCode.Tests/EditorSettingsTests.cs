using Terminal.Gui.Input;
using TuiCode.Abstractions;
using TuiCode.Editor;
using Point = System.Drawing.Point;

namespace TuiCode.Tests;

public class EditorSettingsTests
{
    [Theory]
    [InlineData("", 4, "    ")]
    [InlineData("ab", 4, "ab  ")]
    [InlineData("abcd", 4, "abcd    ")]
    [InlineData("a", 2, "a ")]
    [InlineData("\t", 4, "\t    ")]
    public void Tab_inserts_spaces_to_the_next_tab_stop(string line, int indentSize, string expected)
    {
        var view = View(indentSize, insertSpaces: true, line);
        view.InsertionPoint = new Point(line.Length, 0);

        view.NewKeyDownEvent(Key.Tab);

        Assert.Equal([expected], view.LineStrings);
    }

    [Fact]
    public void Tab_inserts_a_tab_character_when_indenting_with_tabs()
    {
        var view = View(4, insertSpaces: false, "ab");
        view.InsertionPoint = new Point(2, 0);

        view.NewKeyDownEvent(Key.Tab);

        Assert.Equal(["ab\t"], view.LineStrings);
    }

    [Fact]
    public void Tab_replaces_a_selection_with_spaces_to_the_tab_stop_at_its_start()
    {
        var view = View(4, insertSpaces: true, "abXYZ");
        view.SetCarets([new Caret(new Point(5, 0), new Point(2, 0), Extending: true)]);

        view.NewKeyDownEvent(Key.Tab);

        Assert.Equal(["ab  "], view.LineStrings);
    }

    [Fact]
    public void Tab_indents_at_every_caret()
    {
        var view = View(4, insertSpaces: true, "a", "abc");
        view.SetCarets([new Caret(new Point(1, 0)), new Caret(new Point(3, 1))]);

        view.NewKeyDownEvent(Key.Tab);

        Assert.Equal(["a   ", "abc "], view.LineStrings);
    }

    [Theory]
    [InlineData("        ", 8, "    ")]
    [InlineData("      ", 6, "    ")]
    [InlineData("ab  ", 4, "ab")]
    [InlineData("  x ", 4, "  x")]
    [InlineData("ab", 2, "ab")]
    [InlineData("\t\t", 2, "\t")]
    public void Shift_Tab_removes_spaces_back_to_the_previous_tab_stop(string line, int column, string expected)
    {
        var view = View(4, insertSpaces: true, line);
        view.InsertionPoint = new Point(column, 0);

        view.NewKeyDownEvent(Key.Tab.WithShift);

        Assert.Equal([expected], view.LineStrings);
    }

    [Fact]
    public void Tab_is_one_undo_step()
    {
        var view = View(4, insertSpaces: true, "x");
        view.InsertionPoint = new Point(1, 0);

        view.NewKeyDownEvent(Key.Tab);
        view.Undo();

        Assert.Equal(["x"], view.LineStrings);
    }

    [Theory]
    [InlineData(LineEnding.LF, "a\r\nb\r\n", "a\nb\n")]
    [InlineData(LineEnding.CRLF, "a\nb\n", "a\r\nb\r\n")]
    [InlineData(LineEnding.Auto, "a\r\nb\r\n", "a\r\nb\r\n")]
    public void Save_writes_the_chosen_line_ending(LineEnding lineEnding, string original, string expected)
    {
        var (fs, group) = Open(original);
        group.Settings = EditorSettings.Default with { LineEnding = lineEnding };

        group.SaveActive();

        Assert.Equal(expected, fs.File.ReadAllText("/work/x.txt"));
    }

    [Fact]
    public void Save_leaves_the_end_of_the_file_alone_without_insert_final_newline()
    {
        var (fs, group) = Open("hello");
        group.Settings = EditorSettings.Default with { InsertFinalNewline = false };

        group.SaveActive();

        Assert.Equal("hello", fs.File.ReadAllText("/work/x.txt"));
    }

    [Fact]
    public void Settings_apply_to_tabs_opened_later()
    {
        var fs = new MockFileSystem();
        fs.AddFile("/work/x.txt", new MockFileData("a\r\nb"));
        using var group = new EditorGroup { Settings = EditorSettings.Default with { LineEnding = LineEnding.LF } };

        group.OpenOrFocus(fs.FileInfo.New("/work/x.txt"));
        group.SaveActive();

        Assert.Equal("a\nb\n", fs.File.ReadAllText("/work/x.txt"));
    }

    private static (MockFileSystem Fs, EditorGroup Group) Open(string content)
    {
        var fs = new MockFileSystem();
        fs.AddFile("/work/x.txt", new MockFileData(content));
        var group = new EditorGroup();
        group.OpenOrFocus(fs.FileInfo.New("/work/x.txt"));
        return (fs, group);
    }

    private static EditorTextView View(int indentSize, bool insertSpaces, params string[] lines)
    {
        var view = new EditorTextView
        {
            Width = 80,
            Height = 10,
            Text = string.Join("\n", lines),
            TabWidth = indentSize,
            InsertSpaces = insertSpaces,
        };
        view.BeginInit();
        view.EndInit();
        view.Layout();
        return view;
    }
}
