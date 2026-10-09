using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using TuiCode.Editor;
using TuiCode.Explorer;
using TuiCode.Workbench;
using TuiCode.Workbench.Navigation;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Services;
using Point = System.Drawing.Point;

namespace TuiCode.Tests;

// The terminal's own paste arrives as a bracketed paste (#446). Boots a TG Application — serialised (#77).
public class TerminalPasteHostTests : StaticConfigurationTest
{
    [Fact]
    public async Task An_indented_block_goes_in_verbatim_and_one_undo_takes_it_out()
    {
        using var workbench = Workbench();
        using var host = Host(workbench);
        string[] pasted = [];

        await HostSteps.Run(host,
            () => Open(workbench, "    if (x)\nend", At(0, 10)),
            () => Editing(host, workbench),
            () => Paste(host, "\r\n    {\r\n\tcall();\n        nested;\n    }"),
            () => { pasted = [.. Tab(workbench).Lines]; host.App.InjectKey(Key.Z.WithCtrl); });

        Assert.Equal(["    if (x)", "    {", "\tcall();", "        nested;", "    }", "end"], pasted);
        Assert.Equal(["    if (x)", "end"], Tab(workbench).Lines);
        Assert.Equal([At(0, 10)], Tab(workbench).TextView.Carets);
    }

    [Fact]
    public async Task A_paste_replaces_the_selection()
    {
        using var workbench = Workbench();
        using var host = Host(workbench);
        string[] pasted = [];

        await HostSteps.Run(host,
            () => Open(workbench, "one two three", new Caret(new Point(7, 0), new Point(4, 0), Extending: true)),
            () => Editing(host, workbench),
            () => Paste(host, "2\n  2b"),
            () => { pasted = [.. Tab(workbench).Lines]; host.App.InjectKey(Key.Z.WithCtrl); });

        Assert.Equal(["one 2", "  2b three"], pasted);
        Assert.Equal(["one two three"], Tab(workbench).Lines);
    }

    [Theory]
    [InlineData("1\n2", new[] { "a1", "b2" })]
    [InlineData("xy", new[] { "axy", "bxy" })]
    public async Task A_paste_goes_in_at_every_caret_as_ctrl_V_does(string text, string[] expected)
    {
        using var workbench = Workbench();
        using var host = Host(workbench);
        string[] pasted = [];
        string[] undone = [];

        await HostSteps.Run(host,
            () => Open(workbench, "a\nb", At(0, 1), At(1, 1)),
            () => Editing(host, workbench),
            () => Paste(host, text),
            () => { pasted = [.. Tab(workbench).Lines]; host.App.InjectKey(Key.Z.WithCtrl); },
            () =>
            {
                undone = [.. Tab(workbench).Lines];
                host.App.Clipboard!.SetClipboardData(text);
                Tab(workbench).TextView.SetCarets([At(0, 1), At(1, 1)]);
                host.App.InjectKey(Key.V.WithCtrl);
            });

        Assert.Equal(expected, pasted);
        Assert.Equal(["a", "b"], undone);
        Assert.Equal(expected, Tab(workbench).Lines);
    }

    [Fact]
    public async Task A_paste_leaves_the_clipboard_alone()
    {
        using var workbench = Workbench();
        using var host = Host(workbench);

        await HostSteps.Run(host,
            () => { host.App.Driver!.Clipboard = new TestClipboard { Text = "clip" }; Open(workbench, "a", At(0, 1)); },
            () => Editing(host, workbench),
            () => Paste(host, "pasted"));

        Assert.Equal(["apasted"], Tab(workbench).Lines);
        Assert.Equal("clip", host.App.Clipboard!.GetClipboardData());
    }

    [Fact]
    public async Task A_paste_into_the_find_field_goes_there()
    {
        using var workbench = Workbench();
        using var host = Host(workbench);
        var typed = "";

        await HostSteps.Run(host,
            () => Open(workbench, "a needle", At(0, 0)),
            () => host.App.InjectKey(Key.F.WithCtrl),
            () => host.App.Navigation!.GetFocused() is TextField,
            () => Paste(host, "needle"),
            () => { typed = host.App.Navigation!.GetFocused()!.Text; });

        Assert.Equal("needle", typed);
        Assert.Equal(["a needle"], Tab(workbench).Lines);
    }

    [Fact]
    public async Task A_paste_into_a_dialogs_field_goes_there()
    {
        using var workbench = Workbench();
        using var host = Host(workbench);
        var typed = "";

        await HostSteps.Run(host,
            () => Open(workbench, "a\nb\nc", At(0, 0)),
            () => Editing(host, workbench),
            () => { host.App.InjectKey(Key.G.WithCtrl); host.App.InjectKey(Key.L); },
            () => InDialog(host),
            () => Paste(host, "3"),
            () => { typed = host.App.Navigation!.GetFocused()!.Text; host.App.InjectKey(Key.Esc); });

        Assert.Equal("3", typed);
        Assert.Equal(["a", "b", "c"], Tab(workbench).Lines);
    }

    private static bool InDialog(WorkbenchHost host)
    {
        for (var view = host.App.Navigation!.GetFocused(); view is not null; view = view.SuperView)
            if (view is GoToLineView) return true;
        return false;
    }

    private static void Paste(WorkbenchHost host, string text) => Assert.True(host.App.RaisePasteEvent(text));

    private static bool Editing(WorkbenchHost host, Workbench.Workbench workbench) =>
        host.App.Navigation!.GetFocused() == Tab(workbench).TextView;

    private static Caret At(int row, int column) => new(new Point(column, row));

    private static EditorTab Tab(Workbench.Workbench workbench) => workbench.Editor.Group.ActiveTab!;

    private static void Open(Workbench.Workbench workbench, string content, params Caret[] carets)
    {
        var fs = new MockFileSystem();
        fs.AddFile("/work/a.txt", new MockFileData(content));
        workbench.OpenFile(fs.FileInfo.New("/work/a.txt"));
        Tab(workbench).FocusContent();
        Tab(workbench).TextView.SetCarets(carets);
    }

    private static Workbench.Workbench Workbench() =>
        new(new SidebarPart(new FileExplorerView()), new EditorPart(), new StatusBarPart());

    private static WorkbenchHost Host(Workbench.Workbench workbench)
    {
        var commands = new CommandService();
        return new WorkbenchHost(workbench, commands, new KeybindingService(commands), new InputScopeStack(),
            new InMemorySettingsService(), driverName: DriverRegistry.Names.ANSI);
    }
}
