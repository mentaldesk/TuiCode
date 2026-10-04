using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using TuiCode.Abstractions;
using TuiCode.Editor;
using TuiCode.Explorer;
using TuiCode.Syntax;
using TuiCode.Workbench;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Services;
using Point = System.Drawing.Point;

namespace TuiCode.Tests;

public class EditorLineCommentTests
{
    [Fact]
    public void ToggleLineComment_comments_a_line_at_its_indentation_and_a_second_toggle_uncomments_it()
    {
        var view = View("    Start();");
        view.InsertionPoint = new Point(8, 0);

        view.ToggleLineComment("//");
        var commented = view.LineStrings.ToArray();
        view.ToggleLineComment("//");

        Assert.Equal(["    // Start();"], commented);
        Assert.Equal(["    Start();"], view.LineStrings);
        Assert.Equal(new Point(8, 0), view.Carets[0].Position);
    }

    [Fact]
    public void ToggleLineComment_puts_the_marker_at_the_smallest_indentation_of_the_selected_lines()
    {
        var view = View("{", "    Log();", "  Start();", "}");
        view.SetCarets([Selected(new Point(0, 1), new Point(4, 2))]);

        view.ToggleLineComment("//");

        Assert.Equal(["{", "  //   Log();", "  // Start();", "}"], view.LineStrings);
    }

    [Fact]
    public void ToggleLineComment_comments_every_line_when_any_is_uncommented()
    {
        var view = View("// a", "b", "// c");
        view.SetCarets([Selected(new Point(0, 0), new Point(4, 2))]);

        view.ToggleLineComment("//");

        Assert.Equal(["// // a", "// b", "// // c"], view.LineStrings);
    }

    [Fact]
    public void ToggleLineComment_uncomments_every_line_when_all_are_commented_whatever_their_indentation()
    {
        var view = View("  // a", "    //b", "//  c");
        view.SetCarets([Selected(new Point(0, 0), new Point(5, 2))]);

        view.ToggleLineComment("//");

        Assert.Equal(["  a", "    b", " c"], view.LineStrings);
    }

    [Fact]
    public void ToggleLineComment_leaves_blank_lines_alone_either_way()
    {
        var view = View("a", "", "   ", "b");
        view.SetCarets([Selected(new Point(0, 0), new Point(1, 3))]);

        view.ToggleLineComment("#");
        var commented = view.LineStrings.ToArray();
        view.ToggleLineComment("#");

        Assert.Equal(["# a", "", "   ", "# b"], commented);
        Assert.Equal(["a", "", "   ", "b"], view.LineStrings);
    }

    [Fact]
    public void ToggleLineComment_on_a_blank_line_changes_nothing()
    {
        var view = View("a", "", "b");
        view.InsertionPoint = new Point(0, 1);

        view.ToggleLineComment("//");

        Assert.Equal(["a", "", "b"], view.LineStrings);
    }

    [Fact]
    public void ToggleLineComment_keeps_tab_indentation()
    {
        var view = View("\tif (x)", "\t\ty();");
        view.SetCarets([Selected(new Point(0, 0), new Point(5, 1))]);

        view.ToggleLineComment("//");
        var commented = view.LineStrings.ToArray();
        view.ToggleLineComment("//");

        Assert.Equal(["\t// if (x)", "\t// \ty();"], commented);
        Assert.Equal(["\tif (x)", "\t\ty();"], view.LineStrings);
    }

    [Fact]
    public void The_selection_covers_the_same_text_so_a_second_toggle_restores_the_lines()
    {
        var view = View("ab", "cd", "ef");
        view.SetCarets([Selected(new Point(1, 0), new Point(1, 1))]);

        view.ToggleLineComment("--");
        var afterOne = view.Carets[0];
        view.ToggleLineComment("--");

        Assert.Equal(new Point(4, 0), afterOne.Anchor);
        Assert.Equal(new Point(4, 1), afterOne.Position);
        Assert.Equal(["ab", "cd", "ef"], view.LineStrings);
        Assert.Equal(new Point(1, 0), view.Carets[0].Anchor);
        Assert.Equal(new Point(1, 1), view.Carets[0].Position);
    }

    [Fact]
    public void A_selection_of_whole_lines_stays_one_and_leaves_the_line_it_ends_at_alone()
    {
        var view = View("a", "b", "c");
        view.SetCarets([Selected(new Point(0, 0), new Point(0, 2))]);

        view.ToggleLineComment("#");

        Assert.Equal(["# a", "# b", "c"], view.LineStrings);
        Assert.Equal(new Point(0, 0), view.Carets[0].Anchor);
        Assert.Equal(new Point(0, 2), view.Carets[0].Position);
    }

    [Fact]
    public void ToggleLineComment_acts_at_every_caret_as_one_undo_step()
    {
        var view = View("a", "b", "c", "d");
        view.SetCarets([At(0, 0), Selected(new Point(0, 2), new Point(1, 3))]);

        view.ToggleLineComment("//");
        var commented = view.LineStrings.ToArray();
        view.Undo();

        Assert.Equal(["// a", "b", "// c", "// d"], commented);
        Assert.Equal(["a", "b", "c", "d"], view.LineStrings);
        Assert.Equal(2, view.CaretCount);
    }

    [Fact]
    public void ToggleLineComment_comments_every_line_of_a_column_selection()
    {
        var view = View("  a", "  b", "  c");
        view.ColumnSelect = true;
        view.InsertionPoint = new Point(2, 0);
        view.NewKeyDownEvent(Key.CursorDown.WithShift);
        view.NewKeyDownEvent(Key.CursorDown.WithShift);

        view.ToggleLineComment("//");

        Assert.Equal(["  // a", "  // b", "  // c"], view.LineStrings);
    }

    [Fact]
    public void ToggleLineComment_with_block_markers_wraps_a_line_from_its_indentation_and_a_second_toggle_unwraps_it()
    {
        var view = View("  <p>Hi</p>");
        view.InsertionPoint = new Point(5, 0);

        view.ToggleLineComment("<!--", "-->");
        var wrapped = view.LineStrings.ToArray();
        view.ToggleLineComment("<!--", "-->");

        Assert.Equal(["  <!-- <p>Hi</p> -->"], wrapped);
        Assert.Equal(["  <p>Hi</p>"], view.LineStrings);
        Assert.Equal(new Point(5, 0), view.Carets[0].Position);
    }

    [Fact]
    public void ToggleLineComment_with_block_markers_wraps_each_selected_line_at_the_smallest_indentation()
    {
        var view = View("a {", "    color: red;", "", "  margin: 0;", "}");
        view.SetCarets([Selected(new Point(0, 1), new Point(4, 3))]);

        view.ToggleLineComment("/*", "*/");

        Assert.Equal(["a {", "  /*   color: red; */", "", "  /* margin: 0; */", "}"], view.LineStrings);
    }

    [Fact]
    public void ToggleLineComment_with_block_markers_wraps_every_line_when_any_is_unwrapped()
    {
        var view = View("<!-- a -->", "b", "<!-- c -->");
        view.SetCarets([Selected(new Point(0, 0), new Point(10, 2))]);

        view.ToggleLineComment("<!--", "-->");

        Assert.Equal(["<!-- <!-- a --> -->", "<!-- b -->", "<!-- <!-- c --> -->"], view.LineStrings);
    }

    [Fact]
    public void ToggleLineComment_with_block_markers_unwraps_every_line_when_all_are_wrapped()
    {
        var view = View("  <!-- a -->", "    <!--b-->", "<!--  c  -->  ", "", "<!---->");
        view.SetCarets([Selected(new Point(0, 0), new Point(7, 4))]);

        view.ToggleLineComment("<!--", "-->");

        Assert.Equal(["  a", "    b", " c   ", "", ""], view.LineStrings);
    }

    [Fact]
    public void A_line_that_only_starts_or_ends_with_a_marker_is_not_wrapped()
    {
        var view = View("<!-- a --> b", "c <!-- d -->", "<!-->");
        view.SetCarets([Selected(new Point(0, 0), new Point(5, 2))]);

        view.ToggleLineComment("<!--", "-->");

        Assert.Equal(["<!-- <!-- a --> b -->", "<!-- c <!-- d --> -->", "<!-- <!--> -->"], view.LineStrings);
    }

    [Fact]
    public void A_block_wrapped_selection_covers_the_same_text_and_one_undo_restores_every_caret()
    {
        var view = View("ab", "cd", "ef");
        view.SetCarets([Selected(new Point(0, 0), new Point(2, 1)), At(2, 2)]);

        view.ToggleLineComment("/*", "*/");
        var wrapped = view.LineStrings.ToArray();
        var selection = view.Carets[0];
        view.Undo();

        Assert.Equal(["/* ab */", "/* cd */", "/* ef */"], wrapped);
        Assert.Equal(new Point(0, 0), selection.Anchor);
        Assert.Equal(new Point(5, 1), selection.Position);
        Assert.Equal(["ab", "cd", "ef"], view.LineStrings);
        Assert.Equal(2, view.CaretCount);
    }

    [Fact]
    public void A_caret_inside_the_closing_marker_stays_on_the_line_when_it_is_unwrapped()
    {
        var view = View("/* ab */");
        view.InsertionPoint = new Point(7, 0);

        view.ToggleLineComment("/*", "*/");

        Assert.Equal(["ab"], view.LineStrings);
        Assert.Equal(new Point(2, 0), view.Carets[0].Position);
    }

    private static Caret Selected(Point anchor, Point position) => new(position, anchor, Extending: true);

    private static Caret At(int row, int column) => new(new Point(column, row));

    private static EditorTextView View(params string[] lines)
    {
        var view = new EditorTextView { Width = 80, Height = 10, Text = string.Join("\n", lines) };
        view.BeginInit();
        view.EndInit();
        view.Layout();
        return view;
    }
}

public class LineCommentMarkerTests
{
    private static readonly GrammarBundle Bundle = GrammarBundle.Load();

    [Theory]
    [InlineData(".cs", "//")]
    [InlineData(".py", "#")]
    [InlineData(".sh", "#")]
    [InlineData(".yaml", "#")]
    [InlineData(".sql", "--")]
    [InlineData(".lua", "--")]
    [InlineData(".vb", "'")]
    [InlineData(".tex", "%")]
    public void A_bundled_language_has_its_own_line_comment_marker(string extension, string marker)
    {
        Assert.Equal(marker, Bundle.LanguageForFile("file" + extension)!.LineComment);
    }

    [Theory]
    [InlineData(".html", "<!--", "-->")]
    [InlineData(".xml", "<!--", "-->")]
    [InlineData(".md", "<!--", "-->")]
    [InlineData(".css", "/*", "*/")]
    public void A_language_with_only_block_comments_has_its_block_markers_and_no_line_comment_marker(string extension, string open, string close)
    {
        var language = Bundle.LanguageForFile("file" + extension)!;

        Assert.Null(language.LineComment);
        Assert.Equal((open, close), language.BlockComment);
    }
}

// Drives Ctrl+/ through the workbench's command — serialised (#77).
public class EditorLineCommentHostTests : StaticConfigurationTest
{
    private readonly MockFileSystem _fs = new();

    public EditorLineCommentHostTests()
    {
        _fs.AddFile("/work/a.cs", new MockFileData("if (ready)\n    Start();\n"));
        _fs.AddFile("/work/a.txt", new MockFileData("hello\n"));
        _fs.AddFile("/work/a.html", new MockFileData("<p>\n"));
        _fs.AddFile("/work/a.css", new MockFileData("a { color: red; }\n"));
        _fs.AddFile("/work/a.scss", new MockFileData("a { color: red; }\n"));
    }

    [Fact]
    public async Task Ctrl_slash_in_a_CSharp_file_comments_and_uncomments_the_line()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out _);
        EditorTab? tab = null;
        string[] commented = [];

        await HostSteps.Run(host,
            () =>
            {
                tab = workbench.Editor.Open(_fs.FileInfo.New("/work/a.cs"));
                tab.FocusContent();
                tab.MoveCursor(1, 0);
            },
            () => host.App.InjectKey(new Key('/').WithCtrl),
            () => { commented = [.. tab!.Lines]; host.App.InjectKey(new Key('/').WithCtrl); });

        Assert.Equal(["if (ready)", "    // Start();", ""], commented);
        Assert.Equal(["if (ready)", "    Start();", ""], tab!.Lines);
    }

    [Fact]
    public async Task A_language_without_comment_syntax_is_left_alone_and_says_so()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out _);
        EditorTab? tab = null;

        await HostSteps.Run(host,
            () =>
            {
                tab = workbench.Editor.Open(_fs.FileInfo.New("/work/a.txt"));
                tab.FocusContent();
            },
            () => host.App.InjectKey(new Key('/').WithCtrl));

        Assert.Equal("hello", tab!.Lines[0]);
        Assert.Equal("Plain Text has no comment syntax", workbench.StatusBar.Message);
    }

    [Theory]
    [InlineData("/work/a.html", "<!-- <p> -->")]
    [InlineData("/work/a.css", "/* a { color: red; } */")]
    [InlineData("/work/a.scss", "// a { color: red; }")]
    public async Task Ctrl_slash_uses_the_line_comment_and_falls_back_to_the_block_markers(string path, string commented)
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out _);
        EditorTab? tab = null;

        await HostSteps.Run(host,
            () =>
            {
                tab = workbench.Editor.Open(_fs.FileInfo.New(path));
                tab.FocusContent();
            },
            () => host.App.InjectKey(new Key('/').WithCtrl));

        Assert.Equal(commented, tab!.Lines[0]);
        Assert.DoesNotContain("comment syntax", workbench.StatusBar.Message ?? "");
    }

    [Fact]
    public async Task A_rebound_toggle_comment_key_comments_in_the_editor()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out _);
        host.ApplyKeybindings([new KeybindingOverride(TestKeys.Chord("F7"), CommandIds.ToggleLineComment)]);
        EditorTab? tab = null;

        await HostSteps.Run(host,
            () =>
            {
                tab = workbench.Editor.Open(_fs.FileInfo.New("/work/a.cs"));
                tab.FocusContent();
            },
            () => host.App.InjectKey(Key.F7));

        Assert.Equal("// if (ready)", tab!.Lines[0]);
    }

    [Fact]
    public void Toggle_line_comment_is_an_editor_command_in_the_Edit_menu()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);

        var registered = commands.Registered.Single(c => c.Id == CommandIds.ToggleLineComment);

        Assert.Equal("Toggle line comment", registered.Label);
        Assert.Equal(CommandScope.Editor, registered.Scope);
        Assert.Equal("tlc", CommandMnemonics.For(CommandIds.ToggleLineComment));
        Assert.Equal("Ctrl+/", host.Menu.Items.Single(i => i.Id == CommandIds.ToggleLineComment).Item.KeyView.Text);
    }

    private Workbench.Workbench BuildWorkbench()
    {
        var workbench = new Workbench.Workbench(new SidebarPart(new FileExplorerView()),
            new EditorPart(new SyntaxHighlighter(GrammarBundle.Load())), new StatusBarPart());
        workbench.Sidebar.Explorer.Open(_fs.DirectoryInfo.New("/work"));
        return workbench;
    }

    private static WorkbenchHost BuildHost(Workbench.Workbench workbench, out CommandService commands)
    {
        commands = new CommandService();
        return new WorkbenchHost(workbench, commands, new KeybindingService(commands), new InputScopeStack(),
            new InMemorySettingsService(), driverName: DriverRegistry.Names.ANSI);
    }
}
