using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using TuiCode.Abstractions;
using TuiCode.Editor;
using TuiCode.Explorer;
using TuiCode.Workbench;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Services;
using Point = System.Drawing.Point;

namespace TuiCode.Tests;

public class EditorIndentLinesTests
{
    [Fact]
    public void Tab_indents_every_line_a_selection_touches_with_spaces()
    {
        var view = View(4, insertSpaces: true, "a", "  b", "c");
        view.SetCarets([Selected(new Point(1, 0), new Point(2, 1))]);

        view.NewKeyDownEvent(Key.Tab);

        Assert.Equal(["    a", "      b", "c"], view.LineStrings);
    }

    [Fact]
    public void Tab_indents_with_a_tab_character_when_indenting_with_tabs()
    {
        var view = View(4, insertSpaces: false, "a", "b");
        view.SetCarets([Selected(new Point(0, 0), new Point(1, 1))]);

        view.NewKeyDownEvent(Key.Tab);

        Assert.Equal(["\ta", "\tb"], view.LineStrings);
    }

    [Fact]
    public void Tab_leaves_empty_lines_in_the_selection_empty()
    {
        var view = View(2, insertSpaces: true, "a", "", "b");
        view.SetCarets([Selected(new Point(0, 0), new Point(1, 2))]);

        view.NewKeyDownEvent(Key.Tab);

        Assert.Equal(["  a", "", "  b"], view.LineStrings);
    }

    [Fact]
    public void The_selection_covers_the_same_text_so_a_second_Tab_indents_the_same_lines()
    {
        var view = View(4, insertSpaces: true, "ab", "cd", "ef");
        view.SetCarets([Selected(new Point(1, 0), new Point(1, 1))]);

        view.NewKeyDownEvent(Key.Tab);
        var afterOne = view.Carets[0];
        view.NewKeyDownEvent(Key.Tab);

        Assert.Equal(new Point(5, 0), afterOne.Anchor);
        Assert.Equal(new Point(5, 1), afterOne.Position);
        Assert.Equal(["        ab", "        cd", "ef"], view.LineStrings);
    }

    [Fact]
    public void A_selection_ending_at_the_start_of_a_line_leaves_that_line_alone()
    {
        var view = View(4, insertSpaces: true, "a", "b", "c");
        view.SetCarets([Selected(new Point(0, 0), new Point(0, 2))]);

        view.NewKeyDownEvent(Key.Tab);

        Assert.Equal(["    a", "    b", "c"], view.LineStrings);
        Assert.Equal(new Caret(new Point(0, 2), new Point(0, 0), Extending: true), view.Carets[0] with { ColumnTrack = -1 });
    }

    [Fact]
    public void Shift_Tab_with_a_selection_ending_at_the_start_of_a_line_leaves_that_line_alone()
    {
        var view = View(4, insertSpaces: true, "    a", "    b");
        view.SetCarets([Selected(new Point(2, 0), new Point(0, 1))]);

        view.NewKeyDownEvent(Key.Tab.WithShift);

        Assert.Equal(["a", "    b"], view.LineStrings);
    }

    [Theory]
    [InlineData("        x", "    x")]
    [InlineData("      x", "    x")]
    [InlineData("  x", "x")]
    [InlineData("x", "x")]
    [InlineData("\t\tx", "\tx")]
    [InlineData("\t  x", "\tx")]
    [InlineData("  \tx", "x")]
    [InlineData("    \tx", "    x")]
    public void Shift_Tab_outdents_the_caret_line_by_up_to_one_level(string line, string expected)
    {
        var view = View(4, insertSpaces: true, line);
        view.InsertionPoint = new Point(line.Length, 0);

        view.NewKeyDownEvent(Key.Tab.WithShift);

        Assert.Equal([expected], view.LineStrings);
    }

    [Theory]
    [InlineData(10, 6)]
    [InlineData(4, 4)]
    [InlineData(6, 4)]
    [InlineData(0, 0)]
    public void Shift_Tab_keeps_the_caret_on_the_same_character_wherever_it_is_on_the_line(int column, int expected)
    {
        var view = View(4, insertSpaces: true, "        foo");
        view.InsertionPoint = new Point(column, 0);

        view.NewKeyDownEvent(Key.Tab.WithShift);

        Assert.Equal(["    foo"], view.LineStrings);
        Assert.Equal(new Point(expected, 0), view.InsertionPoint);
    }

    [Fact]
    public void Shift_Tab_outdents_every_line_a_selection_touches_and_leaves_unindented_ones_alone()
    {
        var view = View(4, insertSpaces: true, "      a", "  b", "c", "\td");
        view.SetCarets([Selected(new Point(0, 0), new Point(1, 3))]);

        view.NewKeyDownEvent(Key.Tab.WithShift);

        Assert.Equal(["    a", "b", "c", "d"], view.LineStrings);
    }

    [Fact]
    public void Tab_and_Shift_Tab_act_at_every_caret_as_one_undo_step()
    {
        var view = View(2, insertSpaces: true, "a", "b", "c", "d");
        view.SetCarets([At(0, 1), Selected(new Point(0, 2), new Point(1, 3))]);

        view.NewKeyDownEvent(Key.Tab);
        var indented = view.LineStrings.ToArray();
        view.NewKeyDownEvent(Key.Tab.WithShift);
        var outdented = view.LineStrings.ToArray();
        view.Undo();
        var undoneOnce = view.LineStrings.ToArray();
        view.Undo();

        Assert.Equal(["  a", "b", "  c", "  d"], indented);
        Assert.Equal(["a", "b", "c", "d"], outdented);
        Assert.Equal(indented, undoneOnce);
        Assert.Equal(["a", "b", "c", "d"], view.LineStrings);
        Assert.Equal(2, view.CaretCount);
    }

    [Fact]
    public void Shift_Tab_outdents_every_line_of_a_column_selection_as_one_undo_step()
    {
        var view = View(4, insertSpaces: true, "    a", "    b", "    c");
        view.ColumnSelect = true;
        view.InsertionPoint = new Point(5, 0);
        view.NewKeyDownEvent(Key.CursorDown.WithShift);
        view.NewKeyDownEvent(Key.CursorDown.WithShift);

        view.NewKeyDownEvent(Key.Tab.WithShift);
        var outdented = view.LineStrings.ToArray();
        view.Undo();

        Assert.Equal(["a", "b", "c"], outdented);
        Assert.Equal(["    a", "    b", "    c"], view.LineStrings);
    }

    [Fact]
    public void Tab_inserts_at_every_caret_of_a_column_selection()
    {
        var view = View(2, insertSpaces: true, "ab", "cd");
        view.ColumnSelect = true;
        view.InsertionPoint = new Point(1, 0);
        view.NewKeyDownEvent(Key.CursorDown.WithShift);

        view.NewKeyDownEvent(Key.Tab);

        Assert.Equal(["a b", "c d"], view.LineStrings);
    }

    private static Caret Selected(Point anchor, Point position) => new(position, anchor, Extending: true);

    private static Caret At(int row, int column) => new(new Point(column, row));

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

// Drives Tab and Shift+Tab through the workbench's commands — serialised (#77).
public class EditorIndentLinesHostTests : StaticConfigurationTest
{
    private readonly MockFileSystem _fs = new();

    public EditorIndentLinesHostTests() => _fs.AddFile("/work/a.txt", new MockFileData("a\nb\nc\n"));

    [Fact]
    public async Task Tab_and_Shift_Tab_in_the_editor_indent_and_outdent_the_selected_lines()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out _);
        EditorTab? tab = null;
        string[] indented = [];

        await HostSteps.Run(host,
            () =>
            {
                tab = workbench.Editor.Open(_fs.FileInfo.New("/work/a.txt"));
                tab.FocusContent();
                tab.TextView.SetCarets([new Caret(new Point(1, 1), new Point(0, 0), Extending: true)]);
            },
            () => host.App.InjectKey(Key.Tab),
            () => { indented = [.. tab!.Lines]; host.App.InjectKey(Key.Tab.WithShift); });

        Assert.Equal(["    a", "    b", "c", ""], indented);
        Assert.Equal(["a", "b", "c", ""], tab!.Lines);
    }

    [Fact]
    public async Task A_rebound_indent_key_indents_in_the_editor()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out _);
        host.ApplyKeybindings(
        [
            new KeybindingOverride(TestKeys.Chord("Tab"), "-" + CommandIds.IndentLines),
            new KeybindingOverride(TestKeys.Chord("F7"), CommandIds.IndentLines),
        ]);
        EditorTab? tab = null;

        await HostSteps.Run(host,
            () =>
            {
                tab = workbench.Editor.Open(_fs.FileInfo.New("/work/a.txt"));
                tab.FocusContent();
                tab.TextView.SetCarets([new Caret(new Point(1, 1), new Point(0, 0), Extending: true)]);
            },
            () => host.App.InjectKey(Key.F7));

        Assert.Equal(["    a", "    b", "c", ""], tab!.Lines);
    }

    [Fact]
    public async Task Shift_Tab_in_the_find_bar_still_switches_fields()
    {
        _fs.AddFile("/work/b.txt", new MockFileData("    cat cat\n"));
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out _);
        EditorTab? tab = null;

        await HostSteps.Run(host,
            () => { tab = workbench.Editor.Open(_fs.FileInfo.New("/work/b.txt")); },
            () => { tab!.FocusContent(); host.App.InjectKey(Key.H.WithCtrl); },
            () => { foreach (var c in "cat") host.App.InjectKey(new Key(c)); },
            () => host.App.InjectKey(Key.Tab.WithShift),
            () => { foreach (var c in "dog") host.App.InjectKey(new Key(c)); },
            () => host.App.InjectKey(Key.Enter));

        Assert.Equal("    dog cat", tab!.Lines[0]);
    }

    [Fact]
    public void Indent_and_outdent_lines_are_editor_commands_with_mnemonics()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);

        var registered = commands.Registered.Where(c => c.Id is CommandIds.IndentLines or CommandIds.OutdentLines).ToArray();

        Assert.Equal(["Indent lines", "Outdent lines"], registered.Select(c => c.Label));
        Assert.All(registered, c => Assert.Equal(CommandScope.Editor, c.Scope));
        Assert.Equal("il", CommandMnemonics.For(CommandIds.IndentLines));
        Assert.Equal("ol", CommandMnemonics.For(CommandIds.OutdentLines));
        Assert.Equal("Tab", KeyShown(host, CommandIds.IndentLines));
        Assert.Equal("Shift+Tab", KeyShown(host, CommandIds.OutdentLines));
    }

    private static string KeyShown(WorkbenchHost host, string id) => host.Menu.Items.Single(i => i.Id == id).Item.KeyView.Text;

    private Workbench.Workbench BuildWorkbench()
    {
        var workbench = new Workbench.Workbench(new SidebarPart(new FileExplorerView()), new EditorPart(), new StatusBarPart());
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
