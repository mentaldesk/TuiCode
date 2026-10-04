using System.Collections.Concurrent;
using System.Reflection;
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
    [InlineData(".html")]
    [InlineData(".md")]
    public void A_language_with_only_block_comments_has_no_line_comment_marker(string extension)
    {
        Assert.Null(Bundle.LanguageForFile("file" + extension)!.LineComment);
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

    [Theory]
    [InlineData("/work/a.txt", "hello", "Plain Text has no comment syntax")]
    [InlineData("/work/a.html", "<p>", "HTML has no comment syntax")]
    public async Task A_language_without_a_line_comment_is_left_alone_and_says_so(string path, string line, string message)
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

        Assert.Equal(line, tab!.Lines[0]);
        Assert.Equal(message, workbench.StatusBar.Message);
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
    public async Task Ctrl_slash_from_a_terminal_without_the_kitty_protocol_comments_the_line()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out _);
        EditorTab? tab = null;

        await HostSteps.Run(host,
            () =>
            {
                tab = workbench.Editor.Open(_fs.FileInfo.New("/work/a.cs"));
                tab.FocusContent();
            },
            () => host.App.InjectKey(LegacyCtrlSlash));

        Assert.Equal("// if (ready)", tab!.Lines[0]);
    }

    [Fact]
    public async Task A_user_binding_on_the_key_legacy_Ctrl_slash_arrives_as_wins()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out _);
        host.ApplyKeybindings([new KeybindingOverride(TestKeys.Chord("Ctrl+7"), CommandIds.ToggleSidebar)]);
        EditorTab? tab = null;

        await HostSteps.Run(host,
            () =>
            {
                tab = workbench.Editor.Open(_fs.FileInfo.New("/work/a.cs"));
                tab.FocusContent();
            },
            () => host.App.InjectKey(LegacyCtrlSlash));

        Assert.Equal("if (ready)", tab!.Lines[0]);
        Assert.False(workbench.IsSidebarVisible);
        Assert.Equal("Ctrl+/", host.Menu.Items.Single(i => i.Id == CommandIds.ToggleLineComment).Item.KeyView.Text);
    }

    [Fact]
    public async Task Under_the_kitty_protocol_Ctrl_7_is_not_Ctrl_slash()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out _);
        EditorTab? tab = null;

        await HostSteps.Run(host,
            () =>
            {
                KittyFlags(host.App.Driver!, KittyKeyboardFlags.DisambiguateEscapeCodes);
                tab = workbench.Editor.Open(_fs.FileInfo.New("/work/a.cs"));
                tab.FocusContent();
            },
            () => host.App.InjectKey(new Key('/').WithCtrl),
            () => host.App.InjectKey(LegacyCtrlSlash));

        Assert.Equal("// if (ready)", tab!.Lines[0]);
    }

    private static readonly Key LegacyCtrlSlash = Key.D7.WithCtrl;

    private static void KittyFlags(IDriver driver, KittyKeyboardFlags flags)
    {
        if (driver.KittyKeyboardCapabilities is { } capabilities)
        {
            capabilities.Flags = flags;
            return;
        }
        driver.GetType().GetMethod("SetKittyKeyboardCapabilities", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(driver, [new KittyKeyboardCapabilities { IsSupported = true, Flags = flags }]);
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

// What a terminal without the kitty protocol sends for Ctrl+/ turns into under each driver (#388).
public class LegacyCtrlSlashDecodingTests
{
    [Fact]
    public void The_ansi_driver_decodes_0x1F_as_Ctrl_7()
    {
        var queue = new ConcurrentQueue<char>();
        queue.Enqueue('\u001f');

        Assert.Equal(Key.D7.WithCtrl, Decode(new AnsiInputProcessor(queue)));
    }

    [Theory]
    [InlineData('\0', ConsoleKey.D7)]
    [InlineData('\u001f', ConsoleKey.Oem2)]
    public void The_dotnet_driver_decodes_Ctrl_slash_as_Ctrl_7(char keyChar, ConsoleKey consoleKey)
    {
        var queue = new ConcurrentQueue<ConsoleKeyInfo>();
        queue.Enqueue(new ConsoleKeyInfo(keyChar, consoleKey, shift: false, alt: false, control: true));

        Assert.Equal(Key.D7.WithCtrl, Decode(new NetInputProcessor(queue)));
    }

    // The windows driver reads the key itself (VK_OEM_2), not the 0x1F, so it needs no help.
    [Theory]
    [InlineData('\0')]
    [InlineData('\u001f')]
    public void The_windows_driver_decodes_Ctrl_slash_as_itself(char unicodeChar)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The windows driver maps the key through the Win32 keyboard layout");
        var record = new WindowsConsole.InputRecord
        {
            EventType = WindowsConsole.EventType.Key,
            KeyEvent = new WindowsConsole.KeyEventRecord
            {
                bKeyDown = true,
                wRepeatCount = 1,
                wVirtualKeyCode = (VK)ConsoleKey.Oem2,
                UnicodeChar = unicodeChar,
                dwControlKeyState = WindowsConsole.ControlKeyState.LeftControlPressed,
            },
        };
        var converter = Activator.CreateInstance(
            typeof(WindowsConsole).Assembly.GetType("Terminal.Gui.Drivers.WindowsKeyConverter", throwOnError: true)!, nonPublic: true)!;

        Assert.Equal(new Key('/').WithCtrl, (Key)converter.GetType().GetMethod("ToKey")!.Invoke(converter, [record])!);
    }

    private static Key? Decode<T>(InputProcessorImpl<T> processor) where T : struct
    {
        Key? decoded = null;
        processor.KeyDown += (_, key) => decoded = key;
        processor.ProcessQueue();
        return decoded;
    }
}
