using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;
using TuiCode.Abstractions;
using TuiCode.Editor;
using TuiCode.Syntax;
using Point = System.Drawing.Point;

namespace TuiCode.Tests;

// Folding in an editor tab (#477). Renders through a TG driver — serialised (#77).
public class EditorFoldingTests : StaticConfigurationTest
{
    private const int Width = 60;
    private const int Height = 22;

    private static readonly SyntaxHighlighter Syntax = new(GrammarBundle.Load());

    private static readonly string Code = string.Join('\n',
    [
        "using System;",
        "using System.IO;",
        "",
        "public sealed class Workbench",
        "{",
        "    public void Open(string path)",
        "    {",
        "        Read(path);",
        "    }",
        "",
        "    public void Save()",
        "    {",
        "        var tab = Active;",
        "        tab?.Save();",
        "    }",
        "",
        "    #region Diagnostics",
        "    int a;",
        "    #endregion",
        "}",
    ]);

    private const int Open = 5;
    private const int Save = 10;
    private const int SaveBody = 12;
    private const int Region = 16;

    private readonly IApplication _app = Application.Create().Init(DriverRegistry.Names.ANSI);
    private readonly MockFileSystem _fs = new();

    public EditorFoldingTests() => _app.Driver!.SetScreenSize(80, 30);

    public override void Dispose()
    {
        _app.Dispose();
        base.Dispose();
    }

    [Fact]
    public void Folding_a_signature_line_shows_the_method_on_one_row_numbered_from_its_first_line()
    {
        using var tab = Tab("Workbench.cs", Code);
        tab.MoveCursor(Save, 4);

        Assert.True(tab.ToggleFold());
        var screen = Render(tab);

        Assert.Equal(" 11 ▸     public void Save() { ⋯ }", screen[Save].TrimEnd());
        Assert.Equal(" 16", screen[Save + 1].TrimEnd());
        Assert.Equal(" 17 ▾     #region Diagnostics", screen[Save + 2].TrimEnd());
        Assert.True(tab.CursorOnFold);
    }

    [Fact]
    public void Each_line_that_starts_a_block_gets_a_faint_open_marker()
    {
        using var tab = Tab("Workbench.cs", Code);

        var screen = Render(tab);

        var marked = Enumerable.Range(0, 20).Where(row => screen[row][4] == '▾');
        Assert.Equal([0, 3, Open, Save, Region], marked);
    }

    [Fact]
    public void Toggling_on_a_folded_row_unfolds_it()
    {
        using var tab = Tab("Workbench.cs", Code);
        tab.MoveCursor(Save, 4);
        tab.ToggleFold();

        Assert.True(tab.ToggleFold());

        Assert.False(tab.CursorOnFold);
        Assert.Equal("        var tab = Active;", Render(tab)[SaveBody][6..].TrimEnd());
    }

    [Fact]
    public void Toggling_inside_a_body_folds_the_innermost_block_and_leaves_the_cursor_on_its_first_line()
    {
        using var tab = Tab("Workbench.cs", Code);
        tab.MoveCursor(SaveBody, 8);

        tab.ToggleFold();

        Assert.Equal(Save, tab.CursorRow);
        Assert.True(tab.CursorOnFold);
    }

    [Fact]
    public void Fold_all_leaves_the_blocks_around_the_cursor_open()
    {
        using var tab = Tab("Workbench.cs", Code);
        tab.MoveCursor(SaveBody, 8);

        Assert.Equal(3, tab.FoldAll());
        var screen = Render(tab).Select(row => row.TrimEnd()).ToArray();

        Assert.Equal(
        [
            "  1 ▸ using System; ⋯",
            "  3",
            "  4 ▾ public sealed class Workbench",
            "  5   {",
            "  6 ▸     public void Open(string path) { ⋯ }",
            " 10",
            " 11 ▾     public void Save()",
            " 12       {",
            " 13           var tab = Active;",
            " 14           tab?.Save();",
            " 15       }",
            " 16",
            " 17 ▸     #region Diagnostics ⋯ #endregion",
            " 20   }",
        ], screen[..14]);
        Assert.Equal((SaveBody, 8), (tab.CursorRow, tab.CursorColumn));
    }

    [Fact]
    public void Unfold_all_opens_everything_and_counts_it()
    {
        using var tab = Tab("Workbench.cs", Code);
        tab.MoveCursor(SaveBody, 8);
        tab.FoldAll();

        Assert.Equal(3, tab.UnfoldAll());

        Assert.Equal(" 20   }", Render(tab)[19].TrimEnd());
        Assert.Equal(0, tab.UnfoldAll());
    }

    [Fact]
    public void Unfolding_everything_in_a_short_view_keeps_the_cursor_in_it()
    {
        using var tab = Tab("Workbench.cs", Code, height: 8);
        tab.MoveCursor(SaveBody, 8);
        tab.FoldAll();
        Render(tab);

        tab.UnfoldAll();
        Render(tab);

        Assert.InRange(SaveBody - tab.TopRow, 0, tab.VisibleRows - 1);
    }

    [Fact]
    public void Clicking_a_marker_folds_its_block_and_clicking_it_again_unfolds_it()
    {
        using var tab = Tab("Workbench.cs", Code);
        Render(tab);
        var gutter = tab.SubViews.OfType<EditorGutter>().Single();

        gutter.NewMouseEvent(new Mouse { Flags = MouseFlags.LeftButtonClicked, Position = new Point(4, Open) });
        Assert.Equal("  6 ▸     public void Open(string path) { ⋯ }", Render(tab)[Open].TrimEnd());

        gutter.NewMouseEvent(new Mouse { Flags = MouseFlags.LeftButtonClicked, Position = new Point(4, Open) });
        Assert.Equal("  7       {", Render(tab)[Open + 1].TrimEnd());
    }

    [Fact]
    public void Up_and_down_step_over_a_folded_block_as_one_row()
    {
        using var tab = Tab("Workbench.cs", Code);
        tab.MoveCursor(Open, 4);
        tab.ToggleFold();
        Render(tab);

        tab.Code.NewKeyDownEvent(Key.CursorDown);
        Assert.Equal(9, tab.CursorRow);

        tab.Code.NewKeyDownEvent(Key.CursorUp);
        Assert.Equal(Open, tab.CursorRow);
    }

    [Fact]
    public void The_scroll_bar_counts_only_the_rows_you_can_see()
    {
        using var tab = Tab("Workbench.cs", Code);
        tab.MoveCursor(SaveBody, 8);
        tab.FoldAll();
        Render(tab);

        Assert.Equal(14, tab.Code.GetContentSize().Height);
    }

    [Fact]
    public void Page_down_moves_by_visible_rows()
    {
        var lines = Enumerable.Range(0, 60).SelectMany(i => new[] { $"void M{i}()", "{", "    Work();", "}" });
        using var tab = Tab("Long.cs", string.Join('\n', lines));
        tab.FoldAll();
        Render(tab);

        tab.Code.NewKeyDownEvent(Key.PageDown);

        // The cursor's method takes four rows and every other one a row of its own.
        Assert.Equal((tab.VisibleRows - 3) * 4, tab.CursorRow);
    }

    [Fact]
    public void A_jump_into_a_fold_unfolds_it()
    {
        using var tab = Tab("Workbench.cs", Code);
        tab.MoveCursor(SaveBody, 8);
        tab.FoldAll();
        Render(tab);

        tab.MoveCursor(Open + 2, 8);
        tab.RevealLines(Open + 2, Open + 2);
        var screen = Render(tab);

        Assert.False(tab.Code.IsHidden(Open + 2));
        Assert.Contains("  8           Read(path);", screen.Select(row => row.TrimEnd()));
    }

    [Fact]
    public void A_find_match_inside_a_fold_unfolds_it()
    {
        using var tab = Tab("Workbench.cs", Code);
        tab.MoveCursor(SaveBody, 8);
        tab.FoldAll();
        Render(tab);

        tab.Select(new TextMatch(Region + 1, 8, 1));
        Render(tab);

        Assert.False(tab.Code.IsHidden(Region + 1));
        Assert.Equal("a", tab.SelectedText);
    }

    [Fact]
    public void Typing_on_a_folded_row_edits_its_first_line_and_keeps_it_folded()
    {
        using var tab = Tab("Workbench.cs", Code);
        tab.MoveCursor(Save, 4);
        tab.ToggleFold();
        tab.MoveCursor(Save, 16);

        tab.Code.NewKeyDownEvent(Key.A);
        tab.Code.NewKeyDownEvent(Key.Delete);

        Assert.Equal("    public void aave()", tab.Lines[Save]);
        Assert.Equal(" 11 ▸▎    public void aave() { ⋯ }", Render(tab)[Save].TrimEnd());
    }

    [Fact]
    public void Edits_above_a_fold_keep_it_folded_on_its_moved_lines()
    {
        using var tab = Tab("Workbench.cs", Code);
        tab.MoveCursor(Save, 4);
        tab.ToggleFold();
        tab.MoveCursor(2, 0);

        tab.Code.NewKeyDownEvent(Key.Enter);

        Assert.Equal(" 12 ▸     public void Save() { ⋯ }", Render(tab)[Save + 1].TrimEnd());
    }

    [Fact]
    public void Selecting_across_a_folded_row_takes_the_hidden_lines_and_deleting_removes_them()
    {
        using var tab = Tab("Workbench.cs", Code);
        tab.MoveCursor(Open, 4);
        tab.ToggleFold();
        tab.MoveCursor(Open - 1, 1);
        Render(tab);

        tab.Code.NewKeyDownEvent(Key.CursorDown.WithShift);
        tab.Code.NewKeyDownEvent(Key.CursorDown.WithShift);

        Assert.Equal(string.Join('\n', "", "    public void Open(string path)", "    {", "        Read(path);", "    }", ""), tab.SelectedText);
        tab.Code.NewKeyDownEvent(Key.Backspace);
        Assert.Equal(["{", "    public void Save()"], tab.Lines.Skip(4).Take(2));
    }

    [Fact]
    public void A_cursor_added_below_a_folded_row_lands_on_the_next_row_shown()
    {
        using var tab = Tab("Workbench.cs", Code);
        tab.MoveCursor(Open, 4);
        tab.ToggleFold();

        tab.AddCursor(LineDirection.Down);

        Assert.Equal([Open, 9], tab.Code.Carets.Select(c => c.Position.Y).Order());
    }

    [Fact]
    public void Folds_above_and_below_leave_the_sticky_lines_pinned_by_line()
    {
        const int length = 63;
        var methods = Enumerable.Range(0, 3).SelectMany(m => (string[])[$"    void M{m}()", "    {", .. Enumerable.Range(0, length - 3).Select(i => $"        Step({i});"), "    }"]);
        using var tab = Tab("Sticky.cs", string.Join('\n', (string[])["class C", "{", .. methods, "}"]));
        tab.MoveCursor(2, 4);
        tab.ToggleFold();
        tab.MoveCursor(2 + 2 * length, 4);
        tab.ToggleFold();
        tab.MoveCursor(2 + length + 50, 8);
        tab.RevealLines(2 + length + 50, 2 + length + 50);
        RenderUntilPinned(tab);

        Assert.Equal([0, 2 + length], tab.PinnedLines);
    }

    [Fact]
    public void A_markdown_heading_folds_to_the_next_of_its_level_and_wrapped_rows_around_it_still_draw()
    {
        var text = string.Join('\n', "# Title", "", "## One", "a line long enough that it has to wrap onto a second row here", "", "## Two", "end");
        using var tab = Tab("notes.md", text, width: 40);
        tab.WordWrap = true;
        tab.MoveCursor(2, 0);

        tab.ToggleFold();
        var screen = Render(tab).Select(row => row.TrimEnd()).ToArray();

        Assert.Equal(["  1 ▾ # Title", "  2", "  3 ▸ ## One ⋯", "  5", "  6 ▾ ## Two", "  7   end"], screen[..6]);
    }

    [Fact]
    public void A_python_block_folds_by_indentation_leaving_its_trailing_blank_lines_showing()
    {
        using var tab = Tab("a.py", string.Join('\n', "def a():", "    return 1", "", "", "def b():", "    return 2"));

        tab.ToggleFold();
        var screen = Render(tab).Select(row => row.TrimEnd()).ToArray();

        Assert.Equal(["  1 ▸ def a(): ⋯", "  3", "  4", "  5 ▾ def b():"], screen[..4]);
    }

    [Fact]
    public void With_the_gutter_off_a_folded_row_still_shows_what_it_hides()
    {
        using var tab = Tab("Workbench.cs", Code);
        tab.MoveCursor(Save, 4);
        tab.ToggleFold();

        tab.GutterVisible = false;

        Assert.Equal("    public void Save() { ⋯ }", Render(tab)[Save].TrimEnd());
    }

    [Fact]
    public void Reloading_the_file_unfolds_it()
    {
        using var tab = Tab("Workbench.cs", Code);
        tab.MoveCursor(Save, 4);
        tab.ToggleFold();
        _fs.File.WriteAllText("/work/Workbench.cs", Code + "\n");

        tab.Reload();

        Assert.False(tab.Code.IsHidden(SaveBody));
    }

    private void RenderUntilPinned(EditorTab tab)
    {
        for (var i = 0; i < 20 && tab.PinnedLines.Count == 0; i++)
            Render(tab);
    }

    private EditorTab Tab(string name, string content, int width = Width, int height = Height)
    {
        _fs.AddFile($"/work/{name}", new MockFileData(content));
        var tab = new EditorTab(_fs.FileInfo.New($"/work/{name}"), Syntax) { App = _app, Width = width, Height = height };
        tab.BeginInit();
        tab.EndInit();
        tab.Layout();
        return tab;
    }

    private string[] Render(View view)
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
